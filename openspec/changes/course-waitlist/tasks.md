## 1. student-service (backend)

- [ ] 1.1 `Course`: `Capacity`, `SeatsTaken`, `WaitlistCount` y los métodos `TakeSeat` (falla `course.full`), `ReleaseSeat`, `AddToWaitlist` (falla `course.not_full`), `RemoveFromWaitlist` y `PromoteFromWaitlist`; `Course.Create` recibe el cupo (> 0). Verificar: `dotnet build Kindred.slnx`
- [ ] 1.2 `Student`: `WaitlistEntries` (`CourseCode`, `WaitlistedAt`), `JoinWaitlist`, `LeaveWaitlist`, `PromoteFromWaitlist` (agrega un `Enrollment` con `PromotedFromWaitlistAt` y levanta `StudentEnrolledInCourse`), y el máximo de seis contando inscripciones + lista de espera. Errores nuevos en `StudentErrors` con mensajes en español (design D6). Verificar: build
- [ ] 1.3 EF: mapear `Capacity`/`SeatsTaken`/`WaitlistCount` y `xmin` como token de concurrencia en `CourseConfiguration`; `OwnsMany` `waitlist_entries` (clave `StudentId + CourseCode`, índice `CourseCode + WaitlistedAt`); `PromotedFromWaitlistAt` en `enrollments`. Migración `CourseWaitlist` con el relleno de `SeatsTaken` y `Capacity = GREATEST(30, inscritos)` (design, Migration Plan). Verificar: `dotnet ef migrations add CourseWaitlist ...` genera la migración y el servicio arranca contra una base existente
- [ ] 1.4 Repositorios: `ICourseRepository.GetByCodeAsync`, `ICourseRepository.SaveChangesAsync` → `Task<Result>` que traduce `DbUpdateConcurrencyException` a `course.concurrent_change`; `IStudentRepository.GetNextWaitingAsync(courseCode)` (por `WaitlistedAt`, desempate `StudentId`) y `GetWaitlistPositionsAsync(student)`; quitar `CountEnrollmentsByCourseAsync` si queda sin uso. Verificar: build
- [ ] 1.5 Handlers: `EnrollInCourse` carga el curso y ocupa un lugar; comandos nuevos `JoinWaitlist` y `LeaveWaitlist` (sin eventos); `DropCourse` libera el lugar, hace entrar al siguiente y publica en línea `StudentDroppedCourseIntegrationEvent` y `StudentEnrolledInCourseIntegrationEvent` después del guardado (design D4, regla 8). Verificar: build
- [ ] 1.6 DTOs y mapeo: `EnrollmentDto.PromotedFromWaitlistAt`, `WaitlistEntryDto`, `StudentDto.Waitlist`, `CourseDto.Capacity`/`WaitlistCount` (`EnrolledCount` desde `SeatsTaken`); `ToDto(positions)` en los cinco handlers que devuelven un estudiante; `ListCourses` lee los contadores del curso. Verificar: build
- [ ] 1.7 Endpoints `POST /api/v1/students/{id}/waitlist` y `DELETE /api/v1/students/{id}/waitlist/{courseCode}` en el grupo de estudiantes, con el mapeo `ApiError` de siempre. Verificar: con `make kindred-start`, `curl -X POST localhost:11000/api/v1/students/{id}/waitlist` en un curso lleno responde `200`, y en uno con lugar responde `409 course.not_full`
- [ ] 1.8 `DevSeeder`: cupo 30 en todos los cursos y 4 en `ENGL-212`; después de las inscripciones, Leonardo y luego Ximena se anotan en la lista de espera de `ENGL-212` con `JoinWaitlistCommand` (design D7). Verificar: con `make kindred-stop WIPE=1` y luego `make kindred-start`, `GET /api/v1/courses` muestra `ENGL-212` con `enrolledCount: 4`, `capacity: 4`, `waitlistCount: 2`

## 2. Pruebas de student-service

- [ ] 2.1 Pruebas de `Course`: lleno → `course.full`; `AddToWaitlist` con lugar → `course.not_full`; `PromoteFromWaitlist` deja `SeatsTaken` igual y baja `WaitlistCount`. Verificar: `dotnet test tests/Kindred.StudentService.UnitTests`
- [ ] 2.2 Pruebas de `Student`: anotarse dos veces, anotarse estando inscrito, la lista de espera cuenta para el máximo de seis, salirse sin estar, y que la entrada automática saca de la fila, agrega una inscripción con `PromotedFromWaitlistAt` y levanta `StudentEnrolledInCourse`. Verificar: `dotnet test`
- [ ] 2.3 Pruebas de handlers: `DropCourse` con fila publica Dropped + Enrolled (del siguiente) después del guardado, y sin fila publica solo Dropped; `JoinWaitlist`/`LeaveWaitlist` no publican; un choque de guardado devuelve `course.concurrent_change`; ninguna solicitud fallida publica. Verificar: `dotnet test`

## 3. Frontend

- [ ] 3.1 Modelos en `api.models.ts`: `capacity` y `waitlistCount` en el curso; `waitlist` y `promotedFromWaitlistAt` en el estudiante. Verificar: `npm run build --prefix apps/frontend`
- [ ] 3.2 `students.api.ts`: `joinWaitlist(id, courseCode)` y `leaveWaitlist(id, courseCode)`. Verificar: build
- [ ] 3.3 `courses.facade.ts`: `waitlistByCode`, `joinWaitlist`/`leaveWaitlist` (refrescan estudiante y catálogo), `enrolledTotal` cuenta inscripciones + lista de espera, `promotionBanners` y `dismissBanner` con `localStorage` por estudiante dentro de `try/catch` (design D8). Verificar: build
- [ ] 3.4 Página `courses` (`.html`/`.scss`): cupo en cada tarjeta ("4/4"), estado lleno con "· N en espera", "Anotarme en la lista de espera" en un curso lleno, "En espera · #n" + "Salir de la lista de espera" donde espera, y el banner "Se abrió un lugar en {curso} y ya estás inscrito" con ×. Solo tokens y primitivas; cargar `new-module-recipe` antes. Verificar: en el navegador, entrar como Camila, dar de baja `ENGL-212`, entrar como Leonardo y ver el banner; cerrarlo, recargar y comprobar que no vuelve

## 4. Verificación de punta a punta y grafo

- [ ] 4.1 De punta a punta: tras la baja de Camila en la demo, `GET /api/v1/matching/...` (lista del curso `ENGL-212`) ya incluye a Leonardo y no a Camila. Verificar: respuesta de matching-service vía gateway
- [ ] 4.2 Registrar el cambio en el grafo: agregar `course-waitlist` a los `aliases` de `course-enrollment` en `knowledge-graph/capabilities.yaml`, correr `ingest run` + `ingest embed` y confirmar que el cambio no sale como UNBOUND. Verificar: `q query "lista de espera del curso lleno"` devuelve `change:course-waitlist`
- [ ] 4.3 Actualizar `docs/FEATURES.md` (y una página en `docs/features/course-waitlist/` en español) con la lista de espera. Verificar: el enlace de la página existe
