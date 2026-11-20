## Context

Ver proposal.md (Why). Estado actual en student-service:

- `Course` solo tiene código, título y departamento. `enrolledCount` se calcula con
  `IStudentRepository.CountEnrollmentsByCourseAsync` al listar el catálogo.
- `Student` es dueño de sus inscripciones (`OwnsMany`, tabla `enrollments`) y aplica las reglas
  "sin duplicados" y "máximo seis".
- `StudentRepository` y `CourseRepository` comparten el mismo `StudentsDbContext` (scoped), así
  que un solo `SaveChangesAsync` guarda los dos agregados en una transacción.
- No hay tokens de concurrencia en ninguna tabla.
- Cinco handlers devuelven `student.ToDto()`.

El cupo es una regla que abarca a muchos estudiantes, y la lista de espera es una fila por curso.
Ninguna de las dos cabe hoy en un solo agregado.

## Goals / Non-Goals

**Goals:**
- Que el cupo nunca se rebase, ni siquiera con solicitudes simultáneas.
- Que la entrada automática pase en la misma transacción que la baja que la provoca.
- Que matching-service no se entere de la lista de espera: solo ve inscripciones reales.

**Non-Goals:**
- Notificaciones (correo, push). El único aviso es el banner de la página de cursos.
- Editar el cupo desde la API.
- Que el aviso cerrado se recuerde entre navegadores.
- Reintentos automáticos ante un choque de concurrencia: el cliente recibe `409` y vuelve a intentar.

## Decisions

### D1. `Course` lleva los contadores del curso; `Student` lleva la pertenencia
`Course` gana `Capacity`, `SeatsTaken` y `WaitlistCount`, con un token de concurrencia (`xmin` de
PostgreSQL, mapeado con `IsRowVersion`). Sus métodos aplican las reglas del curso:
- `TakeSeat()`: falla con `course.full`.
- `ReleaseSeat()`.
- `AddToWaitlist()`: falla con `course.not_full`.
- `RemoveFromWaitlist()`.
- `PromoteFromWaitlist()`: pasa a uno de la fila a un lugar, y `SeatsTaken` queda igual.

`Student` gana `WaitlistEntries` (`OwnsMany`, tabla `waitlist_entries`, clave `StudentId +
CourseCode`, índice `CourseCode + WaitlistedAt`) y aplica sus propias reglas:
- `JoinWaitlist`
- `LeaveWaitlist`
- `PromoteFromWaitlist`
- el máximo de seis, contando `Enrollments + WaitlistEntries`

*Por qué:* cada operación sobre un curso escribe su fila, así que dos operaciones simultáneas
sobre el mismo curso chocan en el `xmin` y solo una se guarda. Eso protege el cupo y también
evita una carrera más sutil: alguien se anota justo cuando se libera un lugar y queda esperando
en un curso con lugar. Con `WaitlistCount` en `Course`, el catálogo deja de contar con una
consulta.

*Alternativas descartadas:*
- (a) Revisar el conteo en el handler. Deja una carrera abierta y saca la regla del agregado.
- (b) Guardar la fila completa dentro de `Course`. El máximo de seis lo revisa `Student`, así que
  la pertenencia quedaría duplicada en dos agregados.
- (c) Que `Course` lleve solo el cupo y lo demás se cuente con consultas. No da token de
  concurrencia para anotarse ni para salirse de la lista de espera.

### D2. El orden de la fila es una consulta, no un estado
"¿Quién sigue?" es `IStudentRepository.GetNextWaitingAsync(courseCode)`: el estudiante con el
`WaitlistedAt` más antiguo en ese curso, con `StudentId` como desempate. La posición que se
muestra sale de `GetWaitlistPositionsAsync(student)`, que cuenta cuántos llegaron antes en cada
curso. *Por qué:* no se guardan posiciones que habría que renumerar cuando alguien se sale.

### D3. Los choques de concurrencia son un `Result`, no una excepción
`ICourseRepository.SaveChangesAsync` pasa a devolver `Task<Result>`. Captura
`DbUpdateConcurrencyException` y la convierte en `Error.Conflict("course.concurrent_change",
"Alguien más cambió este curso al mismo tiempo. Intenta de nuevo.")`. Los handlers que tocan un
`Course` guardan con ese método. Como el contexto es compartido, también guarda al `Student`. Así
se cumple la regla 4 y la API responde `409` sin código nuevo de manejo de errores.

### D4. Cómo orquesta cada handler
```
EnrollInCourse  : carga student + course → course.TakeSeat → student.Enroll → guardar → publicar Enrolled
JoinWaitlist    : carga student + course → course.AddToWaitlist → student.JoinWaitlist → guardar (sin eventos)
LeaveWaitlist   : carga student + course → student.LeaveWaitlist → course.RemoveFromWaitlist → guardar (sin eventos)
DropCourse      : carga student + course → student.Drop → course.ReleaseSeat
                  next = GetNextWaitingAsync(course)
                  si hay next: course.PromoteFromWaitlist (ocupa el lugar que se liberó) → next.PromoteFromWaitlist
                  guardar → publicar Dropped (student) → publicar Enrolled (next)
```
El orden de las validaciones replica los errores de la spec. Por ejemplo, `already_enrolled` y
`already_waitlisted` los decide `Student`, y `course.not_full` lo decide `Course`. Se pregunta
primero a `Student` cuando su error es más específico.

`Student.PromoteFromWaitlist` saca la entrada de la lista de espera, agrega un `Enrollment` con
`PromotedFromWaitlistAt` y levanta el mismo `StudentEnrolledInCourse` de siempre. Por eso
`DropCourse.cs` construye `new StudentEnrolledInCourseIntegrationEvent(...)` en línea, después del
guardado, con la misma forma que `EnrollInCourse.cs` (regla 8). El grafo verá un segundo
`PUBLISHES` desde `DropCourse.cs`, y matching-service no cambia.

### D5. Contratos de la API
- `EnrollmentDto(CourseCode, EnrolledAt, PromotedFromWaitlistAt?)`
- `WaitlistEntryDto(CourseCode, Position, WaitlistedAt)`
- `StudentDto` + `Waitlist` (ordenada por código del curso)
- `CourseDto(Code, Title, Department, Capacity, EnrolledCount, WaitlistCount)`: `EnrolledCount`
  sale de `SeatsTaken`
- Rutas: `POST /api/v1/students/{id}/waitlist` (`{ courseCode }`) y
  `DELETE /api/v1/students/{id}/waitlist/{courseCode}`, en el grupo de endpoints de estudiantes.
  El gateway ya las cubre.

`ToDto` recibe las posiciones (`ToDto(positions)`). Los cinco handlers que devuelven un
estudiante las piden al repositorio. Si `CountEnrollmentsByCourseAsync` se queda sin usos, se
quita.

### D6. Errores nuevos (mensajes en español)
- `course.full` (Conflict): "Este curso ya está lleno. Puedes anotarte en la lista de espera."
- `course.not_full` (Conflict): "Este curso todavía tiene lugar. Inscríbete directamente."
- `waitlist.already_waitlisted` (Conflict)
- `waitlist.not_waitlisted` (NotFound)
- `course.concurrent_change` (Conflict)

### D7. Seed
El cupo es 30 en todos los cursos, salvo `ENGL-212`, que tiene cupo 4: el seed ya inscribe ahí a
Valeria, Emiliano, Mateo y Camila, así que queda lleno. Después de las inscripciones, Leonardo
(2 cursos) y luego Ximena (3 cursos) se anotan en su lista de espera con `JoinWaitlistCommand`,
el mismo comando que usa la API. Demo: entrar como Camila y dar de baja `ENGL-212`; luego entrar
como Leonardo y ver el banner.

### D8. Frontend
- `courses.facade.ts` expone:
  - `waitlistByCode` (código → posición)
  - `joinWaitlist` / `leaveWaitlist`
  - `promotionBanners`: inscripciones con `promotedFromWaitlistAt` que no están en la lista de cerradas
  - `dismissBanner`
- La lista de cerradas vive en `localStorage` bajo `kindred.waitlist-banner.dismissed.{studentId}`,
  como claves `courseCode@promotedFromWaitlistAt`. Toda lectura y escritura va en `try/catch`.
  Si el almacenamiento falla, el banner sigue funcionando y solo deja de recordarse.
- `enrolledTotal` (el medidor "X de 6") pasa a contar inscripciones + lista de espera, igual que la regla.
- La página no toca `HttpClient` (regla 13). Usa tokens y primitivas existentes (`.alert`,
  `.badge`, `.btn`), sin colores crudos.

## Risks / Trade-offs

- [Dos copias del mismo dato: `SeatsTaken`/`WaitlistCount` en `Course` y las filas en `Student`] →
  Solo se modifican juntas, en la misma transacción y desde los mismos cuatro handlers. Una prueba
  de handler revisa cada camino.
- [Un `409 course.concurrent_change` en uso normal, si dos personas tocan el mismo curso a la vez]
  → En v1 el tráfico es mínimo. El frontend ya muestra el mensaje del `ApiError` y el estudiante
  reintenta.
- [La migración rellena `SeatsTaken` a partir de `enrollments` en bases existentes] → Es un
  `UPDATE` de relleno dentro de la migración, no escritura de la aplicación (la regla 6 aplica al
  código de los servicios). Si en una base existente un curso ya tiene más inscritos que el cupo
  por defecto, `Capacity` se fija en `GREATEST(30, inscritos)`.
- [El aviso cerrado no se recuerda en otro navegador] → Aceptado (opción A). Lo peor que pasa es
  que el banner se vea otra vez.
- [Choque de archivos del frontend con `add-availability-and-group-matching`, que también toca
  "Mis cursos"] → Coordinar el orden de construcción. Este cambio solo agrega elementos a la
  tarjeta del curso, el banner y el medidor.

## Migration Plan

Una migración de EF, `CourseWaitlist`:
- `courses`: agrega `Capacity` (default 30), `SeatsTaken` (rellenado desde `enrollments`) y
  `WaitlistCount` (0).
- `enrollments`: agrega `PromotedFromWaitlistAt` (nullable).
- crea `waitlist_entries`.

El servicio la aplica al arrancar (regla 11). Para revertir: el `Down` quita columnas y tabla.
Después de cambiar código, `make kindred-start` reconstruye las imágenes. Con
`make kindred-stop WIPE=1` el seed vuelve a correr con los cupos nuevos.
