# classmate-discovery Specification

## Purpose
Descubrir compañeros: un estudiante ve quién más toma sus cursos, ordenados por cursos en común,
para encontrar con quién estudiar. Es el corazón de Kindred v1. Lo administra student-service
(módulo Students) y se muestra en la página Compañeros del frontend.

## Requirements

### Requirement: Listar compañeros
El sistema MUST devolver, para un estudiante, a todos los demás estudiantes que comparten al menos
un curso con él, junto con sus cursos en común.

#### Scenario: Cursos en común
- **WHEN** se llama a `GET /api/v1/students/{id}/classmates`
- **THEN** cada elemento trae `studentId`, `displayName`, `program`, `sharedCourses` (códigos de curso) y `sharedCount`
- **AND** el propio estudiante nunca aparece en la lista

#### Scenario: Todavía sin cursos
- **WHEN** el estudiante no está inscrito en ningún curso
- **THEN** la respuesta es `200` con una lista vacía

#### Scenario: Estudiante desconocido
- **WHEN** el id no pertenece a ningún estudiante
- **THEN** la respuesta es `404` con `code: student.not_found`

### Requirement: Ordenar por cursos en común
El sistema MUST ordenar a los compañeros por número de cursos en común, de mayor a menor, y por
nombre cuando el número es igual.

#### Scenario: Orden
- **WHEN** un compañero comparte tres cursos y otro comparte uno
- **THEN** el compañero que comparte tres cursos aparece primero

### Requirement: La página Compañeros agrupa por curso
El frontend MUST mostrar a los compañeros agrupados bajo cada curso del estudiante, con una
insignia en cada compañero que indica cuántos cursos en común tienen en total.

#### Scenario: Vista agrupada
- **WHEN** un estudiante que entró abre la página Compañeros
- **THEN** hay un grupo por cada curso inscrito con los compañeros que lo toman
- **AND** un compañero que comparte varios cursos aparece en cada uno de esos grupos con el mismo número de cursos en común
