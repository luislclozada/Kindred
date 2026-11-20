## Context

Ver proposal.md (Why). El nombre del estudiante tiene una fuente de verdad, el aggregate
`Student` en student-service, y una copia en matching-service: `RosterMember.StudentName`
dentro de cada `CourseRoster`, escrita al recibir `StudentEnrolledInCourseIntegrationEvent`.
`CourseRoster.Add` ignora a un miembro que ya existe, así que volver a inscribirse no corrige el
nombre. La carrera no se copia a matching-service.

## Goals / Non-Goals

**Goals:**
- Editar nombre y carrera desde student-service con la regla en el aggregate.
- Que la copia del nombre en matching-service se corrija por evento, sin HTTP entre servicios.

**Non-Goals:**
- Editar el correo (es la identidad única del estudiante).
- Historial de cambios del perfil.
- Garantizar el orden de cambios de nombre entregados fuera de orden (ver Risks).

## Decisions

1. **`PUT /api/v1/students/{id}` con `{ displayName, program }`**, devuelve `StudentDto`.
   Reemplaza los dos campos editables; es más simple que `PATCH` con campos opcionales. La ruta
   ya pasa por el gateway (`/api/v1/students/{**rest}`), sin cambios en YARP.

2. **`Student.UpdateProfile(displayName, program, now)` devuelve `Result`.** Usa las mismas
   validaciones que `Register` (compartidas dentro del aggregate) y los mismos errores
   (`DisplayNameRequired`, `ProgramRequired`). Solo levanta el evento de dominio `StudentRenamed`
   si el nombre recortado es distinto del actual.

3. **Evento `StudentRenamedIntegrationEvent(EventId, OccurredAt, StudentId, DisplayName)`**, no
   un `StudentProfileUpdated` genérico. Matching solo necesita el nombre; un evento preciso evita
   mensajes inútiles cuando solo cambia la carrera. Si algún día otro servicio necesita la
   carrera, se agrega su propio evento. El handler `UpdateStudentProfile` lo construye inline
   después de `SaveChangesAsync`, igual que `EnrollInCourse.cs` (regla 8: la forma que lee el grafo).

4. **En matching-service:** `ICourseRosterRepository.ListContainingStudentAsync(studentId)` carga
   las listas donde está el estudiante (6 como máximo); `CourseRoster.RenameMember(studentId, name)`
   cambia el nombre del miembro (no hace nada si no está o si ya tiene ese nombre). El handler
   `StudentRenamedHandler` vive junto a los de inscripción y guarda una sola vez. Asignar un
   valor es idempotente por naturaleza; no hace falta una tabla de eventos procesados.
   Alternativa descartada: un `UPDATE` masivo en SQL, porque rompe la regla 6.

5. **Frontend:** página `profile` (ruta `/profile`, título "Kindred - Mi perfil", con
   `signedInGuard`) → `ProfileFacade` → `StudentsApi.updateProfile`. Al guardar, el facade
   entrega el `StudentDto` devuelto a `SessionFacade` (método nuevo, p. ej. `replaceStudent`),
   que actualiza su signal y con ello el encabezado. Enlace "Mi perfil" en el encabezado.
   Se sigue `new-module-recipe`.

## Risks / Trade-offs

- [Dos cambios de nombre seguidos entregados fuera de orden dejan en matching el penúltimo] →
  Aceptado en v1: una sola cola, un solo consumidor, y el caso exige dos ediciones casi
  simultáneas. Si hiciera falta, se guarda `OccurredAt` por miembro y se ignoran los eventos más
  viejos, sin cambiar el contrato.
- [Un evento de inscripción viejo con el nombre anterior llega después del cambio] → No afecta:
  `Add` ignora a un miembro que ya existe.
- [Un estudiante se inscribe mientras su cambio de nombre está en vuelo] → El evento de
  inscripción ya lleva el nombre nuevo, porque se lee del aggregate al inscribirse.
- [matching-service no tiene proyecto de pruebas] → La regla de `RenameMember` es trivial; se
  verifica de punta a punta (tarea de verificación). No se crea un proyecto nuevo (regla 16).

## Migration Plan

Sin migraciones de base de datos. Despliegue: `make kindred-start` reconstruye los servicios;
matching-service arranca primero y declara el binding del evento nuevo antes de que
student-service pueda publicarlo. Rollback: revertir el commit; un evento sin consumidor se
descarta sin efecto.
