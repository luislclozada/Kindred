## Why

Hoy un estudiante solo puede escribir su nombre y su carrera al registrarse; si se equivocó o
cambió de carrera, no hay forma de corregirlo. Además, su nombre vive en dos lugares: en
student-service (el perfil) y en matching-service (una copia en cada lista del curso, recibida
con el evento de inscripción). Editar solo el perfil dejaría el nombre viejo en las listas del
curso para siempre.

**Revisión del grafo de conocimiento** (`q query "¿existe editar el perfil del estudiante,
cambiar nombre y carrera?"`, `q detect-duplicates`): no se encontró cobertura existente para
editar el perfil. La superficie más cercana es la capacidad `student-profiles`
(`knowledge-graph/capabilities.yaml@ead3bb40`, spec `openspec/specs/student-profiles/spec.md@ead3bb40`),
que hoy solo registra, lista y consulta estudiantes. Este cambio **extiende** esa spec; no crea
una capacidad nueva. `detect-duplicates`: sin candidatos.

## What Changes

- **student-service** (dominio, aplicación, API): el estudiante puede editar su nombre y su carrera
  con `PUT /api/v1/students/{id}`. Se aplican las mismas validaciones que en el registro. El
  correo no se puede editar.
- **Evento de integración nuevo** `StudentRenamedIntegrationEvent` (en
  `Kindred.SharedKernel/Messaging/Events/`): lo publica student-service cuando el nombre
  cambia de verdad; lo consume matching-service.
- **matching-service** (dominio, aplicación, datos): al recibir el evento, actualiza el nombre del
  estudiante en todas las listas del curso donde aparece. El manejo es idempotente.
- **Frontend**: página nueva "Mi perfil" para editar nombre y carrera; al guardar, el nombre del
  encabezado cambia de inmediato.
- **Gateway**: sin rutas nuevas; `/api/v1/students/{**rest}` ya cubre el endpoint.
- **Grafo de conocimiento**: un puente nuevo `Matching → Students` por `student-renamed`.

## Capabilities

### New Capabilities

_(ninguna)_

### Modified Capabilities

- `student-profiles`: nuevo requisito "Editar el perfil" (endpoint, validaciones, evento de cambio de nombre) y la página "Mi perfil".
- `course-rosters`: nuevo requisito "La lista del curso refleja los cambios de nombre".

## Impact

- `src/Services/StudentService/Kindred.StudentService` — `Student.UpdateProfile`, evento de dominio `StudentRenamed`, comando `UpdateStudentProfile`.
- `src/Services/StudentService/Kindred.StudentService.Api/Endpoints/StudentEndpoints.cs` — `PUT /{id:guid}`.
- `src/SharedKernel/Kindred.SharedKernel/Messaging/Events/` — contrato `StudentRenamedIntegrationEvent`.
- `src/Services/MatchingService/` — `CourseRoster.RenameMember`, consulta del repositorio por estudiante, handler del evento nuevo y su registro.
- `apps/frontend` — `students.api.ts`, facade y página de perfil, ruta y enlace en el encabezado, actualización de `session.facade.ts`.
- Sin migraciones: las columnas ya existen en las dos bases.
- `knowledge-graph/capabilities.yaml` — alias del cambio y puente nuevo.
