## ADDED Requirements

### Requirement: Cada curso tiene un cupo
Cada curso del catálogo de cursos MUST tener un cupo: un número entero positivo de lugares. El
cupo se carga con el seed. El sistema MUST NOT exponer ningún endpoint que lo edite.

#### Scenario: Cupo en el catálogo
- **WHEN** se llama a `GET /api/v1/courses`
- **THEN** cada curso trae un `capacity` mayor que cero
- **AND** `enrolledCount` nunca es mayor que `capacity`

## MODIFIED Requirements

### Requirement: Listar el catálogo de cursos
El sistema MUST listar todos los cursos con su código, título, departamento, cupo, el número de
estudiantes inscritos en ese momento y el número de estudiantes en su lista de espera, ordenados
por código del curso.

#### Scenario: Catálogo con conteos
- **WHEN** se llama a `GET /api/v1/courses`
- **THEN** cada elemento trae `code`, `title`, `department`, `capacity`, `enrolledCount` y `waitlistCount`
- **AND** `enrolledCount` refleja las inscripciones y las bajas hechas hasta ese momento, incluidas las entradas automáticas desde la lista de espera
- **AND** `waitlistCount` refleja quién se anotó y quién se salió o entró desde la lista de espera

### Requirement: El catálogo de cursos se carga con datos seed
En el ambiente Development, el sistema MUST cargar al arrancar el catálogo con el cupo de cada
curso (y un grupo de estudiantes de demo) en una base de datos vacía. El seed MUST dejar
exactamente un curso lleno con estudiantes de demo en su lista de espera; todos los demás cursos
MUST quedar con lugar. El sistema MUST NOT exponer ningún endpoint que edite el catálogo.

#### Scenario: Primer arranque
- **WHEN** student-service arranca en Development con una base de datos vacía
- **THEN** después existen el catálogo y los estudiantes de demo
- **AND** los estudiantes de demo se inscribieron con los mismos comandos que usa la API, así que sus eventos de inscripción se publicaron

#### Scenario: Un curso lleno para la demo
- **WHEN** student-service termina el seed
- **THEN** exactamente un curso tiene `enrolledCount` igual a su `capacity` y un `waitlistCount` mayor que cero
- **AND** esos estudiantes de demo se anotaron en la lista de espera con el mismo comando que usa la API
