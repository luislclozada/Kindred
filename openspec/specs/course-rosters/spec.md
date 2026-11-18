# course-rosters Specification

## Purpose
matching-service mantiene su propia versión de quién toma cada curso, la lista del curso, para que
la formación de grupos de estudio se construya sobre ella sin preguntarle nunca a student-service.
Lo administra matching-service (módulo Matching).

## Requirements

### Requirement: La lista del curso se construye con eventos de inscripción
matching-service MUST mantener una lista por curso a partir de los eventos de integración de
student-service y MUST NOT llamar a student-service por HTTP.

#### Scenario: El estudiante se inscribe
- **WHEN** llega un `StudentEnrolledInCourseIntegrationEvent`
- **THEN** el estudiante (id y nombre) está en la lista de ese curso

#### Scenario: El estudiante da de baja el curso
- **WHEN** llega un `StudentDroppedCourseIntegrationEvent`
- **THEN** el estudiante ya no está en la lista de ese curso

### Requirement: El manejo de eventos es idempotente
Procesar el mismo evento más de una vez MUST dejar la lista del curso sin cambios.

#### Scenario: Reentrega
- **WHEN** el mismo evento de inscripción se entrega dos veces
- **THEN** el estudiante aparece una sola vez en la lista

### Requirement: Consultar la lista del curso
El sistema MUST devolver la lista de un curso, ordenada por nombre.

#### Scenario: Lista con estudiantes
- **WHEN** se llama a `GET /api/v1/matching/courses/{courseCode}/roster`
- **THEN** la respuesta es `200` con `courseCode` (en mayúsculas) y `students` (`studentId`, `displayName`)

#### Scenario: Curso que nadie toma
- **WHEN** ningún estudiante se ha inscrito en ese curso
- **THEN** la respuesta es `200` con una lista `students` vacía
