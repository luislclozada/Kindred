# course-enrollment Specification

## Purpose
Un estudiante se inscribe en cursos del catálogo de cursos y puede darlos de baja después. Las
reglas de inscripción viven en el agregado `Student`. Cada inscripción y cada baja se anuncia al
resto del sistema como un evento de integración. Lo administra student-service (módulo Students).

## Requirements

### Requirement: Inscribirse en un curso
El sistema MUST permitir que un estudiante se inscriba en un curso que existe en el catálogo y
MUST devolver el estudiante actualizado.

#### Scenario: Inscripción exitosa
- **WHEN** se envía `POST /api/v1/students/{id}/enrollments` con `{ "courseCode": "COMP-248" }`
- **THEN** la respuesta es `200` con el estudiante, cuyas inscripciones ahora incluyen `COMP-248`

#### Scenario: Estudiante o curso desconocido
- **WHEN** el estudiante no existe, o el código del curso está bien formado pero no está en el catálogo
- **THEN** la respuesta es `404` con `code: student.not_found` o `code: course.not_found`

### Requirement: Sin inscripción duplicada
El sistema MUST rechazar la inscripción de un estudiante en un curso que ya toma.

#### Scenario: Ya inscrito
- **WHEN** un estudiante se inscribe en un curso en el que ya está inscrito
- **THEN** la respuesta es `409` con `code: enrollment.already_enrolled`

### Requirement: Máximo seis cursos
El sistema MUST rechazar una inscripción que dejaría a un estudiante con más de seis cursos.

#### Scenario: Séptimo curso
- **WHEN** un estudiante con seis inscripciones se inscribe en otro curso
- **THEN** la respuesta es `400` con `code: enrollment.course_limit_reached`

### Requirement: Dar de baja un curso
El sistema MUST permitir que un estudiante dé de baja un curso en el que está inscrito y MUST
devolver el estudiante actualizado.

#### Scenario: Baja exitosa
- **WHEN** se llama a `DELETE /api/v1/students/{id}/enrollments/{courseCode}` para un curso inscrito
- **THEN** la respuesta es `200` con el estudiante, cuyas inscripciones ya no incluyen ese curso

#### Scenario: No inscrito
- **WHEN** el estudiante no está inscrito en ese curso
- **THEN** la respuesta es `404` con `code: enrollment.not_enrolled`

### Requirement: Los cambios de inscripción se publican
Después de guardar una inscripción o una baja, student-service MUST publicar
`StudentEnrolledInCourseIntegrationEvent` o `StudentDroppedCourseIntegrationEvent` en RabbitMQ.
MUST NOT publicar nada para una solicitud que falló.

#### Scenario: Evento de inscripción
- **WHEN** una inscripción tiene éxito
- **THEN** se publica un `StudentEnrolledInCourseIntegrationEvent` con el id del estudiante, el nombre del estudiante y el código del curso

#### Scenario: Evento de baja
- **WHEN** una baja tiene éxito
- **THEN** se publica un `StudentDroppedCourseIntegrationEvent` con el id del estudiante y el código del curso
