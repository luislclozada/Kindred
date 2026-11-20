## Context

Ver proposal.md (Why). Estado actual relevante:

- matching-service tiene un solo aggregate, `CourseRoster` (`Domain/CourseRoster.cs`): por curso,
  la lista de `(StudentId, StudentName)`, construida solo con los eventos de inscripción, de baja y
  de cambio de nombre de student-service (`Application/EnrollmentEventHandlers.cs`). Ya existe
  `ICourseRosterRepository.ListContainingStudentAsync`.
- matching-service aplica sus migraciones al arrancar
  (`DataServiceCollectionExtensions.cs`) y expone sus endpoints bajo `/api/v1/matching`, que el
  gateway ya enruta.
- No hay proyecto de pruebas de matching-service; solo `tests/Kindred.StudentService.UnitTests`.
- El frontend ya tiene `MatchingApi` (`core/api/matching.api.ts`), la página `courses` y el
  encabezado con su lista de enlaces en `layout/header/header.ts`.
- Cambio en paralelo: `course-waitlist` (otra sesión) edita `pages/courses/**`,
  `courses.facade.ts`, `students.api.ts` y `api.models.ts`, y publica el
  `StudentEnrolledInCourseIntegrationEvent` de siempre cuando alguien sale de la lista de espera
  (desde `DropCourse.cs`, después del `StudentDroppedCourseIntegrationEvent`). No cambia contratos
  ni matching-service. Ver la decisión 7.

## Goals / Non-Goals

**Goals:**
- Que matching-service sea dueño de la disponibilidad y forme los grupos sin preguntarle nada a
  student-service y sin contratos de evento nuevos.
- Que un grupo conserve a las mismas personas: los grupos se guardan y solo cambian por una razón
  del propio integrante (baja, o disponibilidad que ya no coincide).
- Que las reglas de los grupos vivan en un aggregate y se prueben sin infraestructura.
- Un solo editor de disponibilidad, en Mis cursos.

**Non-Goals:**
- Un reparto óptimo de grupos. Colocar sin reacomodar es suficiente y explicable.
- Unirse o salirse de un grupo a mano.
- Validar que el id del estudiante exista (decisión de v1; ver Risks).
- Datos de ejemplo de disponibilidad en el seed.

## Decisions

### 1. La disponibilidad vive en matching-service, no en el perfil
El único que la usa es la formación de grupos: cada cambio a los bloques o a cómo coinciden nace
de las reglas de agrupamiento, así que el módulo Matching es el único que tiene razones para
cambiarla. Guardarla en el aggregate `Student` obligaría a crear un
`StudentAvailabilityChangedIntegrationEvent`, un consumidor idempotente, una copia en
matching-service y un puente nuevo `Matching → Students`. Ponerla en matching-service evita los
cuatro, y el spine ya liga `availability-*` a `study-group-matching`.
*Alternativa descartada:* dueño student-service + evento. Funciona, pero agrega piezas sin dar
nada a cambio en v1. Si otra capacidad llega a necesitar la disponibilidad, se mueve entonces.

### 2. Un aggregate `StudentAvailability` por estudiante, con bloques como value object
- `AvailabilityBlock(DayOfWeek Day, TimeSlot Slot)` es un value object; `TimeSlot` es un enum
  `Morning | Afternoon | Evening`. Se serializan como `MONDAY` / `MORNING` (regla 10).
- `StudentAvailability` (`Id = studentId`) tiene `Replace(IEnumerable<(string day, string slot)>)`
  que devuelve `Result`: interpreta los textos, rechaza cualquier valor desconocido con
  `availability.invalid_block` sin modificar nada, quita repetidos y guarda el conjunto.
- **Por qué texto y no enum en el DTO del request:** si el request usara enums, un valor
  desconocido fallaría en la deserialización con un 400 genérico del framework, sin `ApiError`
  (regla 5). Con texto, la validación es del aggregate y el error es un `Error.Validation`.
- Persistencia: `OwnsMany` en una tabla hija `availability_blocks (student_id, day, slot)`, igual
  que `RosterMember`. *Alternativa descartada:* una máscara de 21 bits en una columna, más
  compacta pero ilegible en la base de datos y más difícil de explicar.
- Si el estudiante no tiene fila, la consulta devuelve `blocks: []`; el `PUT` crea la fila si no
  existe.

### 3. Los grupos se guardan dentro de `CourseRoster`
Las reglas de los grupos son invariantes entre todos los grupos de un curso (nadie está en dos,
nadie entra a uno lleno), así que pertenecen al aggregate por curso que ya existe:

```
CourseRoster (Id = courseCode)
  Members : RosterMember (StudentId, StudentName)     ← ya existe
  Groups  : StudyGroup (Id, FormedAt, MemberIds)      ← nuevo, entidad interna
```

- `StudyGroup` guarda solo quiénes son sus integrantes y cuándo se formó (de `TimeProvider`). Los
  bloques en común **no** se guardan: se calculan con la disponibilidad actual al consultar.
- El aggregate no conoce repositorios. Las operaciones reciben la disponibilidad de los
  integrantes de la lista como `IReadOnlyDictionary<Guid, IReadOnlySet<AvailabilityBlock>>`
  (quien no aparece no tiene disponibilidad).

Operaciones nuevas o ampliadas:

| Operación | Regla |
|---|---|
| `Place(studentId, availability)` | Si ya tiene grupo o no tiene bloques, no hace nada (idempotente). Si no: entra al grupo más antiguo con < 4 integrantes cuya intersección de bloques con todos tenga algo en común con los suyos; si no hay, abre un grupo nuevo con compañeros sin grupo que compartan un bloque (orden por nombre ordinal sin distinguir mayúsculas y luego por id; se agregan mientras la intersección no quede vacía, hasta cuatro); si nadie coincide, queda sin grupo. Nunca mueve a otro integrante. |
| `Remove(studentId, availability)` | Ya existe; ahora además saca al estudiante de su grupo. Si el grupo queda con uno, lo disuelve y llama a `Place` para quien quedó. |
| `Reconsider(studentId, availability)` | Tras un cambio de disponibilidad: si sigue compartiendo un bloque con todo su grupo, no cambia nada; si no, sale (con la misma regla de disolución) y se le llama `Place`. Si no tenía grupo, `Place`. |
| `RenameMember` | Sin cambios: no toca grupos. |

*Alternativa descartada (la versión anterior de este diseño):* calcular los grupos al consultar
con un servicio de dominio puro. Es más simple, pero cualquier inscripción o cambio de nombre
reacomoda a todos, y el usuario pidió que el grupo conserve a las mismas personas.

### 4. Quién llama a esas operaciones
Todo pasa dentro de matching-service; ningún evento nuevo.

| Disparador | Handler | Qué hace |
|---|---|---|
| `StudentEnrolledInCourseIntegrationEvent` | `StudentEnrolledInCourseHandler` (ya existe) | `roster.Add(...)`, carga la disponibilidad de los integrantes y llama `roster.Place(...)` |
| `StudentDroppedCourseIntegrationEvent` | `StudentDroppedCourseHandler` (ya existe) | carga la disponibilidad y llama `roster.Remove(...)` |
| `StudentRenamedIntegrationEvent` | `StudentRenamedHandler` (ya existe) | sin cambios |
| `PUT .../availability` | `SaveStudentAvailabilityCommand` (nuevo) | `availability.Replace(...)`; luego, para cada lista que contiene al estudiante (`ListContainingStudentAsync`), carga la disponibilidad de sus integrantes y llama `roster.Reconsider(...)`; guarda todo en un solo `SaveChanges` |

La disponibilidad de los integrantes se carga con
`IStudentAvailabilityRepository.ListForStudentsAsync(ids)` en una sola consulta por lista.

### 5. La consulta "mis grupos"
`GetStudentGroupsQuery(studentId)`: carga las listas que contienen al estudiante; en cada una toma
su grupo guardado, los nombres de los integrantes desde `Members` y calcula `commonBlocks` como la
intersección de la disponibilidad actual del grupo. Devuelve un elemento por curso, ordenados por
código, sin el propio estudiante, o vacío si no tiene grupo. Es solo lectura.

### 6. Endpoints
En un archivo nuevo `Endpoints/StudentEndpoints.cs` (matching-service), bajo `/students/{id:guid}`:
`GET /availability`, `PUT /availability` y `GET /groups`. El `PUT` devuelve 200 con el DTO, o 400
con `ApiError`. Un id que no es GUID no coincide con la ruta (404 del framework), igual que en
student-service.

### 7. Frontend, compatible con `course-waitlist`
- `MatchingApi` recibe `availability(id)`, `saveAvailability(id, blocks)` y `groups(id)`.
- Los tipos nuevos (`Day`, `TimeSlot`, `AvailabilityBlock`, `Availability`, `StudentGroup`,
  `GroupMember`) van en un archivo propio, `core/models/matching.models.ts`, para no editar
  `api.models.ts`, que `course-waitlist` también modifica.
- `AvailabilityFacade` (`providedIn: 'root'`) guarda la disponibilidad del estudiante actual en
  signals, la recarga cuando cambia la sesión y expone `toggle`, `save`, `saving` y `error`. Es de
  raíz porque también la lee Mis grupos (el aviso "sin disponibilidad").
- `shared/availability-editor/` es un componente standalone (`.ts` / `.html` / `.scss`) que solo
  habla con su facade: cuadrícula 7 × 3 con casillas accesibles (`button` con `aria-pressed`) y un
  botón Guardar. **No toca `courses.facade.ts`.** En `pages/courses/` solo se agregan el import en
  `courses.page.ts` y una etiqueta `<app-availability-editor />` en `courses.page.html`, como
  última tarea del frontend. No espera a `course-waitlist` (que solo está propuesto): el cambio
  que llegue después hace el merge encima de esas dos líneas.
- `GroupsFacade` y `pages/groups/` ("Mis grupos", ruta `/groups`, `signedInGuard`), con los cuatro
  estados de vista de `new-module-recipe`. El aviso "sin disponibilidad" lleva a Mis cursos.
- Las etiquetas en español de días y franjas viven en un solo lugar (`core/util/availability.ts`).

### 8. Pruebas
Proyecto nuevo `tests/Kindred.MatchingService.UnitTests` (xUnit, como el de student-service),
agregado a `Kindred.slnx`. Cubre `StudentAvailability.Replace`, `CourseRoster.Place` / `Remove` /
`Reconsider` (los escenarios de la spec `study-groups`) y los handlers mediante interfaces falsas.

## Risks / Trade-offs

- [Colocar sin reacomodar puede dejar fuera a alguien que cabría con otro acomodo, y el resultado
  depende del orden en que llegan los estudiantes] → Se acepta: es lo que da estabilidad, y se
  explica en una frase.
- [Guardar la disponibilidad modifica varias `CourseRoster` mientras un evento de inscripción
  puede estar modificando la misma] → `CourseRoster` lleva un token de concurrencia (`xmin` de
  PostgreSQL). Si un evento choca, falla y RabbitMQ lo reentrega (los handlers son idempotentes);
  si el `PUT` choca, responde `409` con `ApiError` (`availability.conflict`) y el estudiante vuelve
  a guardar. Con el tamaño de Kindred es raro.
- [Los bloques en común se calculan con la disponibilidad actual] → Siempre hay al menos uno,
  porque cualquier cambio de disponibilidad pasa por `Reconsider`.
- [Se acepta cualquier id: pueden quedar filas de disponibilidad de estudiantes que no existen] →
  No afectan a los grupos, porque solo se colocan integrantes de las listas del curso. Se revisa
  cuando haya autenticación.
- [Sin disponibilidad en el seed, Mis grupos aparece vacía al iniciar] → El aviso de la página
  lleva a indicar la disponibilidad; para una demo basta con marcarla para dos o tres estudiantes.
- [`course-waitlist` publica una baja y luego una inscripción en el mismo momento] → Son de
  estudiantes distintos y los dos handlers son independientes; el orden entre ellos no cambia el
  resultado de ninguna regla.

## Migration Plan

- Una migración de EF en `Kindred.MatchingService.Data` (`AddAvailabilityAndStudyGroups`) crea
  `student_availabilities`, `availability_blocks`, `study_groups` y `study_group_members`, y el
  token de concurrencia de `course_rosters`. matching-service la aplica al arrancar;
  `make kindred-start` reconstruye la imagen.
- Las listas que ya existen quedan sin grupos hasta que sus estudiantes guarden disponibilidad. No
  se borra ni se transforma nada. Revertir es quitar la migración y el código.
