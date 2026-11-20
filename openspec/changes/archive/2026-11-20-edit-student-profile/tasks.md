## 1. Contrato compartido

- [x] 1.1 Agregar `StudentRenamedIntegrationEvent(EventId, OccurredAt, StudentId, DisplayName)` en `Kindred.SharedKernel/Messaging/Events/` (archivo propio o junto a los de inscripción) y verificar que `dotnet build Kindred.slnx` compila

## 2. student-service (backend)

- [x] 2.1 Agregar `Student.UpdateProfile(displayName, program, now)` que devuelve `Result`, comparte las validaciones de `Register` y levanta el evento de dominio `StudentRenamed` solo si el nombre cambia; verificar con el build
- [x] 2.2 Agregar el comando `UpdateStudentProfileCommand` y su handler en `Application/Commands/UpdateStudentProfile.cs`: cargar, `UpdateProfile`, guardar y después publicar `new StudentRenamedIntegrationEvent(...)` inline por cada `StudentRenamed`; devuelve `Result<StudentDto>`; verificar con el build
- [x] 2.3 Mapear `PUT /api/v1/students/{id:guid}` en `StudentEndpoints.cs` con un request `{ displayName, program }` (200 con `StudentDto`; 400 / 404 con `ApiError`); verificar con el build

## 3. Pruebas de student-service

- [x] 3.1 Pruebas de `Student.UpdateProfile` en `tests/Kindred.StudentService.UnitTests`: edición exitosa con recorte de espacios, nombre vacío, carrera vacía, solo cambia la carrera (sin evento), cambia el nombre (con evento), correo e inscripciones intactos; verificar con `dotnet test tests/Kindred.StudentService.UnitTests`
- [x] 3.2 Pruebas del handler `UpdateStudentProfile`: estudiante desconocido → `student.not_found`; publica el evento de integración solo si cambió el nombre y después de guardar; verificar con `dotnet test tests/Kindred.StudentService.UnitTests`

## 4. matching-service (backend)

- [x] 4.1 Agregar `CourseRoster.RenameMember(studentId, name)` (sin efecto si el miembro no está o ya tiene ese nombre) y un método en `RosterMember` para cambiar el nombre; verificar con el build
- [x] 4.2 Agregar `ICourseRosterRepository.ListContainingStudentAsync(studentId)` y su implementación en `Kindred.MatchingService.Data` (carga los miembros); verificar con el build
- [x] 4.3 Agregar `StudentRenamedHandler : IIntegrationEventHandler<StudentRenamedIntegrationEvent>` en `EnrollmentEventHandlers.cs` y registrarlo con `AddIntegrationEventHandler` en `Program.cs`; verificar con el build

## 5. Frontend

- [x] 5.1 Cargar el skill `new-module-recipe`; agregar `updateProfile(id, { displayName, program })` a `students.api.ts` y el request a `api.models.ts`; verificar con `npm run build --prefix apps/frontend`
- [x] 5.2 Agregar a `SessionFacade` un método que reemplaza el estudiante actual (el encabezado lo lee de ahí); verificar con el build — se reusó `SessionFacade.setStudent`, que ya reemplaza al estudiante; no hizo falta método nuevo
- [x] 5.3 Crear `ProfileFacade` y la página `pages/profile/` (`.ts` / `.html` / `.scss` separados, OnPush, signals, primitivas y tokens compartidos) con los campos nombre y carrera, estado de guardado y el mensaje del `ApiError`; al guardar actualiza la sesión; verificar con el build — además valida en el navegador (requerido, máx. 80) como el registro; spec ajustada
- [x] 5.4 Agregar la ruta `/profile` ("Kindred - Mi perfil", `signedInGuard`) en `app.routes.ts` y el enlace "Mi perfil" en el encabezado; verificar con el build

## 6. Verificación de punta a punta y grafo

- [x] 6.1 Con `make kindred-start`: entrar como un estudiante inscrito en dos cursos, cambiar su nombre en Mi perfil y comprobar que el encabezado, el selector "entrar como", la página Compañeros de otro estudiante y `GET /api/v1/matching/courses/{code}/roster` de ambos cursos muestran el nombre nuevo — verificado por API (5 listas del curso, selector, compañeros); el encabezado se comprobó por código, no en navegador
- [x] 6.2 Cambiar solo la carrera y comprobar en la UI de RabbitMQ (11673) que no se publicó ningún `StudentRenamedIntegrationEvent` — contador `publish` de `matching-service.events`: 3 ediciones, 2 mensajes
- [x] 6.3 `ingest run` y comprobar que el grafo muestra `student-service -[PUBLISHES]-> StudentRenamedIntegrationEvent -[CONSUMED_BY]-> matching-service` sin nada UNBOUND
