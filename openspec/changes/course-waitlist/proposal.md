## Why

Un curso del catálogo de cursos hoy no tiene cupo: cualquier estudiante puede inscribirse en
cualquier curso, sin límite. Queremos que un curso pueda llenarse y que, cuando eso pase, un
estudiante pueda anotarse en una lista de espera y entrar automáticamente en cuanto otro dé de
baja el curso, sin tener que estar revisando a mano.

**Revisión del grafo de conocimiento** (`q query "lista de espera del curso lleno: cupo del curso
e inscripción automática al dar de baja" --kind existence`, `q detect-duplicates`): no se
encontró cobertura existente. No hay spec, cambio activo ni código de cupo o de lista de espera.
Lo más cercano:

- La capacidad `course-enrollment` (`knowledge-graph/capabilities.yaml@5c3ff164`), dueña del
  catálogo de cursos y de la inscripción en el módulo Students. Este cambio la **extiende**; no
  crea una capacidad nueva.
- La spec `course-enrollment` (`openspec/specs/course-enrollment/spec.md@5c3ff164`): sus reglas
  de inscripción (sin duplicados, máximo seis cursos, dar de baja) son las que se amplían.
- La spec `course-catalog` (`openspec/specs/course-catalog/spec.md@5c3ff164`): el listado del
  catálogo gana el cupo y el tamaño de la lista de espera. El catálogo sigue sin endpoints de
  edición.

`detect-duplicates`: sin candidatos relacionados (el único resultado es la pareja ya conocida
`change:edit-student-profile` ↔ `feature:edit-student-profile`).

El cambio en vuelo `add-availability-and-group-matching` vive en matching-service y no se
traslapa en el backend. Puede tocar los mismos archivos del frontend (encabezado, "Mis cursos"),
así que conviene coordinar el orden si los dos se construyen a la vez.

## What Changes

- **student-service** (dominio, aplicación, datos, API):
  - Cada curso tiene un cupo (se carga con el seed; no hay endpoint para editarlo). Inscribirse
    en un curso lleno se rechaza con `409 course.full`.
  - Un estudiante puede anotarse en la lista de espera de un curso lleno con
    `POST /api/v1/students/{id}/waitlist` y salirse con
    `DELETE /api/v1/students/{id}/waitlist/{courseCode}`. Anotarse es una acción explícita: nunca
    se mete a nadie en la fila sin pedirlo.
  - Estar en una lista de espera cuenta para el máximo de seis cursos.
  - Cuando un estudiante da de baja un curso con lista de espera, el primero de la fila (por
    orden de llegada) queda inscrito automáticamente en el mismo momento.
  - El estudiante devuelto por la API incluye su lista de espera (con su posición) y, en cada
    inscripción, si entró desde la lista de espera. El catálogo de cursos incluye el cupo y
    cuántos esperan.
  - Columnas nuevas y su migración de EF. El seed deja un curso lleno con estudiantes de demo en
    su lista de espera.
- **Eventos de integración**: ninguno nuevo y ningún contrato cambia. La entrada automática
  publica el `StudentEnrolledInCourseIntegrationEvent` de siempre (desde la baja), además del
  `StudentDroppedCourseIntegrationEvent` de quien dio de baja. Anotarse o salirse de la lista de
  espera no publica nada.
- **matching-service**: sin cambios. Sigue consumiendo los mismos dos eventos, así que la lista
  del curso se actualiza sola cuando alguien entra desde la lista de espera.
- **gateway**: sin rutas nuevas (`/api/v1/students/{**rest}` ya cubre `/waitlist`).
- **Frontend**: la página de cursos muestra el cupo, marca los cursos llenos, ofrece "Anotarme en
  la lista de espera" y "Salir de la lista de espera" con la posición en la fila, y un banner
  "Se abrió un lugar en … y ya estás inscrito" que se cierra con × y se recuerda en el navegador.
  Sin notificaciones.

## Capabilities

### New Capabilities

_Ninguna._

### Modified Capabilities

- `course-enrollment`: inscribirse en un curso lleno se rechaza; nuevos requisitos de lista de
  espera (anotarse, salirse, posición, entrada automática al dar de baja); el máximo de seis
  cursos incluye la lista de espera; la entrada automática se publica como inscripción.
- `course-catalog`: el listado del catálogo incluye el cupo y el número de estudiantes en lista
  de espera; el seed carga el cupo de cada curso.

## Impact

- `src/Services/StudentService/`: agregados `Course` (cupo, lugares ocupados) y `Student`
  (entradas en lista de espera, entrada automática); comandos `EnrollInCourse`, `DropCourse` y
  dos nuevos (anotarse / salirse); `ListCourses`; DTOs; errores; repositorios; configuración de
  EF y una migración; `DevSeeder`; endpoints.
- `apps/frontend/`: modelos, `students.api.ts`, `courses.facade.ts`, página `courses`.
- `tests/Kindred.StudentService.UnitTests`: reglas nuevas de los agregados y los handlers.
- API: dos endpoints nuevos y campos nuevos (aditivos) en `CourseDto` y `StudentDto`. Un cambio
  de comportamiento: `POST /enrollments` en un curso lleno ahora responde `409`.
