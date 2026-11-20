# student-profiles Specification

## Purpose
En Kindred, cada estudiante existe como un perfil: un nombre, un correo único y una carrera. En
v1 no hay autenticación: el perfil se crea al registrarse y se elige con un selector "entrar
como". Lo administra student-service (módulo Students).

## Requirements

### Requirement: Registrar un estudiante
El sistema MUST permitir que cualquiera registre un estudiante con nombre, correo y carrera, y
MUST devolver el estudiante nuevo con una lista de inscripciones vacía.

#### Scenario: Registro exitoso
- **WHEN** se envía `POST /api/v1/students` con un nombre, un correo válido y una carrera
- **THEN** la respuesta es `201 Created` con el estudiante (`id`, `displayName`, `email`, `program`, `enrollments: []`)
- **AND** el correo se guarda sin espacios al inicio ni al final y en minúsculas

#### Scenario: Faltan datos
- **WHEN** el nombre o la carrera están vacíos, o el correo no tiene forma de correo
- **THEN** la respuesta es `400` con un `ApiError` cuyo `code` nombra el problema (`student.display_name_required`, `student.program_required`, `student.invalid_email`)

### Requirement: Los correos son únicos
El sistema MUST rechazar el registro de un segundo estudiante con un correo que ya está
registrado. La comparación no distingue mayúsculas de minúsculas.

#### Scenario: Correo duplicado
- **WHEN** un registro usa un correo que ya tiene otro estudiante
- **THEN** la respuesta es `409` con `code: student.email_taken`
- **AND** no se crea ningún estudiante

### Requirement: Listar estudiantes
El sistema MUST listar a todos los estudiantes como resumen, ordenados por nombre, para que la UI
pueda ofrecer un selector "entrar como".

#### Scenario: Datos del selector
- **WHEN** se llama a `GET /api/v1/students`
- **THEN** cada elemento trae `id`, `displayName`, `program` y `courseCount`

### Requirement: Consultar un estudiante
El sistema MUST devolver un estudiante con sus inscripciones.

#### Scenario: Estudiante existente
- **WHEN** se llama a `GET /api/v1/students/{id}` para un estudiante que existe
- **THEN** la respuesta es `200` con el estudiante y sus inscripciones (`courseCode`, `enrolledAt`), ordenadas por código del curso

#### Scenario: Estudiante desconocido
- **WHEN** el id no pertenece a ningún estudiante
- **THEN** la respuesta es `404` con `code: student.not_found`

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
