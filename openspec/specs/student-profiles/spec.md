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
