## ADDED Requirements

### Requirement: Editar el perfil
El sistema MUST permitir que un estudiante cambie su nombre y su carrera, con las mismas
validaciones que el registro, y MUST devolver el estudiante actualizado. El correo MUST NOT
cambiar con esta operación.

#### Scenario: Edición exitosa
- **WHEN** se envía `PUT /api/v1/students/{id}` con un `displayName` y un `program` no vacíos
- **THEN** la respuesta es `200` con el estudiante (`id`, `displayName`, `email`, `program`, `enrollments`) ya actualizado
- **AND** el nombre y la carrera se guardan sin espacios al inicio ni al final
- **AND** el correo y las inscripciones no cambian

#### Scenario: Faltan datos
- **WHEN** el nombre o la carrera están vacíos
- **THEN** la respuesta es `400` con un `ApiError` cuyo `code` es `student.display_name_required` o `student.program_required`
- **AND** el perfil no cambia

#### Scenario: Estudiante desconocido
- **WHEN** el id no pertenece a ningún estudiante
- **THEN** la respuesta es `404` con `code: student.not_found`

#### Scenario: Los valores nuevos se ven en todas las consultas de student-service
- **WHEN** un estudiante cambió su nombre o su carrera
- **THEN** `GET /api/v1/students`, `GET /api/v1/students/{id}` y la lista de compañeros de los demás estudiantes muestran los valores nuevos

### Requirement: Avisar los cambios de nombre
Cuando el nombre de un estudiante cambia, student-service MUST publicar un
`StudentRenamedIntegrationEvent` (`studentId`, `displayName`) después de guardar. Si el nombre
no cambió, MUST NOT publicarlo.

#### Scenario: Cambia el nombre
- **WHEN** una edición del perfil cambia el nombre
- **THEN** se publica un `StudentRenamedIntegrationEvent` con el nombre nuevo

#### Scenario: Solo cambia la carrera
- **WHEN** una edición del perfil deja el mismo nombre y cambia la carrera
- **THEN** no se publica ningún `StudentRenamedIntegrationEvent`

### Requirement: La página Mi perfil
El frontend MUST ofrecer una página "Mi perfil" al estudiante que entró, con su nombre y su
carrera para editarlos, y al guardar MUST mostrar el nombre nuevo en el encabezado sin recargar.

#### Scenario: Guardar el perfil
- **WHEN** un estudiante que entró cambia su nombre en Mi perfil y guarda
- **THEN** el encabezado muestra el nombre nuevo de inmediato

#### Scenario: Campo vacío
- **WHEN** el estudiante deja el nombre o la carrera vacíos
- **THEN** la página marca el campo con un error y no envía el cambio, igual que el formulario de registro

#### Scenario: El servidor rechaza el cambio
- **WHEN** el servidor responde con un `ApiError` (por ejemplo, un nombre con solo espacios)
- **THEN** la página muestra el mensaje del `ApiError` y conserva lo que escribió
