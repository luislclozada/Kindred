# course-catalog Specification

## Purpose
El catálogo de cursos es la lista de cursos en los que un estudiante puede inscribirse. En v1 es
de solo lectura: se carga una vez con datos seed y nunca se edita desde la API. Lo administra
student-service (módulo Students).

## Requirements

### Requirement: El código del curso tiene un solo formato
El sistema MUST identificar cada curso con un código de tres o cuatro letras, un guion y tres
dígitos (por ejemplo `COMP-248`). El código MUST guardarse y devolverse siempre en mayúsculas.

#### Scenario: Normalización
- **WHEN** un código del curso llega como `comp-248` o con espacios alrededor
- **THEN** se trata como `COMP-248`

#### Scenario: Código mal formado
- **WHEN** un código del curso no cumple el formato (por ejemplo `COMP248` o `banana`)
- **THEN** la solicitud se rechaza con `400` y `code: course.invalid_code`

### Requirement: Listar el catálogo de cursos
El sistema MUST listar todos los cursos con su código, título, departamento y el número de
estudiantes inscritos en ese momento, ordenados por código del curso.

#### Scenario: Catálogo con conteos
- **WHEN** se llama a `GET /api/v1/courses`
- **THEN** cada elemento trae `code`, `title`, `department` y `enrolledCount`
- **AND** `enrolledCount` refleja las inscripciones y las bajas hechas hasta ese momento

### Requirement: El catálogo de cursos se carga con datos seed
En el ambiente Development, el sistema MUST cargar al arrancar el catálogo (y un grupo de
estudiantes de demo) en una base de datos vacía. El sistema MUST NOT exponer ningún endpoint que
edite el catálogo.

#### Scenario: Primer arranque
- **WHEN** student-service arranca en Development con una base de datos vacía
- **THEN** después existen el catálogo y los estudiantes de demo
- **AND** los estudiantes de demo se inscribieron con los mismos comandos que usa la API, así que sus eventos de inscripción se publicaron
