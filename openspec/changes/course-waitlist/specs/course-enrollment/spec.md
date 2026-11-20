## ADDED Requirements

### Requirement: Un curso lleno no acepta inscripciones
El sistema MUST rechazar la inscripción en un curso cuyo número de inscritos ya alcanzó su cupo.
Dos inscripciones simultáneas por el último lugar MUST resolverse con una sola inscripción
exitosa: el número de inscritos MUST NOT superar el cupo nunca.

#### Scenario: Curso lleno
- **WHEN** un estudiante se inscribe en un curso cuyo `enrolledCount` es igual a su `capacity`
- **THEN** la respuesta es `409` con `code: course.full`
- **AND** no se publica ningún evento

#### Scenario: Último lugar disputado
- **WHEN** dos estudiantes se inscriben al mismo tiempo en un curso al que le queda un lugar
- **THEN** uno recibe `200` y el otro `409`
- **AND** el `enrolledCount` del curso es igual a su `capacity`

### Requirement: Anotarse en la lista de espera
El sistema MUST permitir que un estudiante se anote en la lista de espera de un curso lleno y
MUST devolver el estudiante actualizado. Anotarse MUST ser siempre una acción explícita del
estudiante: una inscripción rechazada por curso lleno MUST NOT anotarlo en la lista de espera.

#### Scenario: Anotarse con éxito
- **WHEN** se envía `POST /api/v1/students/{id}/waitlist` con `{ "courseCode": "ENGL-212" }` y el curso está lleno
- **THEN** la respuesta es `200` con el estudiante, cuya lista de espera ahora incluye `ENGL-212` con su posición en la fila

#### Scenario: El curso tiene lugar
- **WHEN** un estudiante se anota en la lista de espera de un curso que no está lleno
- **THEN** la respuesta es `409` con `code: course.not_full`

#### Scenario: Ya está en la lista de espera
- **WHEN** un estudiante se anota en la lista de espera de un curso en cuya lista ya está
- **THEN** la respuesta es `409` con `code: waitlist.already_waitlisted`

#### Scenario: Ya inscrito
- **WHEN** un estudiante se anota en la lista de espera de un curso en el que ya está inscrito
- **THEN** la respuesta es `409` con `code: enrollment.already_enrolled`

#### Scenario: Estudiante o curso desconocido
- **WHEN** el estudiante no existe, o el código del curso está bien formado pero no está en el catálogo
- **THEN** la respuesta es `404` con `code: student.not_found` o `code: course.not_found`

#### Scenario: Anotarse no se publica
- **WHEN** un estudiante se anota en una lista de espera
- **THEN** no se publica ningún evento de integración

### Requirement: Salirse de la lista de espera
El sistema MUST permitir que un estudiante se salga de una lista de espera en la que está y MUST
devolver el estudiante actualizado. Los que estaban detrás de él MUST avanzar un lugar.

#### Scenario: Salirse con éxito
- **WHEN** se llama a `DELETE /api/v1/students/{id}/waitlist/{courseCode}` para un curso en cuya lista de espera está
- **THEN** la respuesta es `200` con el estudiante, cuya lista de espera ya no incluye ese curso
- **AND** quien estaba en la posición siguiente pasa a la posición que él dejó

#### Scenario: No está en la lista de espera
- **WHEN** el estudiante no está en la lista de espera de ese curso
- **THEN** la respuesta es `404` con `code: waitlist.not_waitlisted`

#### Scenario: Salirse no se publica
- **WHEN** un estudiante se sale de una lista de espera
- **THEN** no se publica ningún evento de integración

### Requirement: Posición en la lista de espera
La lista de espera de un curso MUST ordenarse por orden de llegada. Todo estudiante que devuelve
la API MUST incluir su lista de espera: para cada curso, `courseCode`, `position` (empezando en
1) y `waitlistedAt`.

#### Scenario: Orden de llegada
- **WHEN** Ana se anota en la lista de espera de `ENGL-212` y después Luis se anota en la misma lista
- **THEN** la lista de espera de Ana muestra `ENGL-212` en `position: 1` y la de Luis en `position: 2`

### Requirement: Entrada automática desde la lista de espera
Cuando un estudiante da de baja un curso que tiene lista de espera, el sistema MUST inscribir en
ese curso, en la misma operación, al primero de la fila. Esa entrada lo saca de la lista de espera.
La inscripción que resulta MUST llevar `promotedFromWaitlistAt` con el momento en que entró. Una
inscripción hecha directamente MUST llevar `promotedFromWaitlistAt` en `null`.

#### Scenario: Se libera un lugar
- **WHEN** un estudiante da de baja `ENGL-212`, que está lleno y tiene a Luis primero en la lista de espera
- **THEN** Luis queda inscrito en `ENGL-212` con `promotedFromWaitlistAt` y ya no está en su lista de espera
- **AND** el `enrolledCount` del curso sigue igual a su `capacity`
- **AND** los demás de la fila avanzan un lugar

#### Scenario: Sin lista de espera
- **WHEN** un estudiante da de baja un curso sin nadie en la lista de espera
- **THEN** el `enrolledCount` del curso baja en uno y nadie más queda inscrito

### Requirement: La página de cursos muestra la lista de espera
La página de cursos del frontend MUST mostrar en cada curso su cupo y, si está lleno, cuántos
esperan. En un curso lleno MUST ofrecer "Anotarme en la lista de espera"; en un curso donde el
estudiante espera, su posición y "Salir de la lista de espera". MUST mostrar un banner
"Se abrió un lugar en {curso} y ya estás inscrito" por cada inscripción con
`promotedFromWaitlistAt` que el estudiante no haya cerrado. Al cerrarlo, MUST NOT volver a
mostrarse en ese navegador para ese estudiante.

#### Scenario: Curso lleno en la página
- **WHEN** un estudiante que entró abre la página de cursos y un curso está lleno
- **THEN** ese curso se ve como lleno, con cuántos esperan, y ofrece "Anotarme en la lista de espera" en lugar de "Inscribirme"

#### Scenario: Banner de entrada automática
- **WHEN** un estudiante que entró desde la lista de espera abre la página de cursos
- **THEN** ve el banner "Se abrió un lugar en ENGL-212 y ya estás inscrito"
- **AND** al cerrarlo con × y recargar la página, el banner ya no aparece

## MODIFIED Requirements

### Requirement: Máximo seis cursos
El sistema MUST rechazar una inscripción, o una entrada a una lista de espera, que dejaría a un
estudiante con más de seis cursos entre los que está inscrito y aquellos en cuya lista de espera
está. Una entrada automática desde la lista de espera no cambia ese total.

#### Scenario: Séptimo curso
- **WHEN** un estudiante con seis inscripciones se inscribe en otro curso
- **THEN** la respuesta es `400` con `code: enrollment.course_limit_reached`

#### Scenario: La lista de espera cuenta
- **WHEN** un estudiante con cinco inscripciones y una lista de espera se inscribe en otro curso o se anota en otra lista de espera
- **THEN** la respuesta es `400` con `code: enrollment.course_limit_reached`

#### Scenario: La entrada automática siempre cabe
- **WHEN** un estudiante con cinco inscripciones y una lista de espera entra automáticamente a ese curso
- **THEN** queda con seis inscripciones y ninguna lista de espera

### Requirement: Los cambios de inscripción se publican
Después de guardar una inscripción o una baja, student-service MUST publicar
`StudentEnrolledInCourseIntegrationEvent` o `StudentDroppedCourseIntegrationEvent` en RabbitMQ.
Una entrada automática desde la lista de espera MUST publicarse como inscripción, después de
guardar, junto con la baja que la provocó. MUST NOT publicar nada para una solicitud que falló.

#### Scenario: Evento de inscripción
- **WHEN** una inscripción tiene éxito
- **THEN** se publica un `StudentEnrolledInCourseIntegrationEvent` con el id del estudiante, el nombre del estudiante y el código del curso

#### Scenario: Evento de baja
- **WHEN** una baja tiene éxito
- **THEN** se publica un `StudentDroppedCourseIntegrationEvent` con el id del estudiante y el código del curso

#### Scenario: Evento de entrada automática
- **WHEN** una baja hace entrar a Luis desde la lista de espera
- **THEN** se publica el `StudentDroppedCourseIntegrationEvent` de quien dio de baja y un `StudentEnrolledInCourseIntegrationEvent` con el id, el nombre de Luis y el código del curso
