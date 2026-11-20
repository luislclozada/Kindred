## Purpose

La formación de grupos de estudio: en cada curso, los estudiantes de la lista del curso que
coinciden en al menos un bloque de disponibilidad forman grupos de hasta cuatro, y cada grupo
conserva a las mismas personas mientras sigan coincidiendo. La administra matching-service
(módulo Matching) y se muestra en la página Mis grupos.

## ADDED Requirements

### Requirement: Formar grupos de estudio por curso
Para cada curso, el sistema MUST formar grupos de estudio con los estudiantes de la lista del curso
que tienen al menos un bloque de disponibilidad. Todos los integrantes de un grupo MUST compartir
al menos un bloque en común. Un grupo MUST tener entre dos y cuatro integrantes, y un estudiante
MUST pertenecer a lo más a un grupo por curso. Un estudiante sin pareja posible queda sin grupo:
el sistema MUST NOT guardar ni mostrar grupos de un solo integrante.

#### Scenario: Coinciden en un bloque
- **WHEN** dos estudiantes de la lista del mismo curso tienen disponibilidad el lunes en la tarde
- **THEN** los dos están en el mismo grupo de ese curso, con el lunes en la tarde entre sus bloques en común

#### Scenario: No coinciden
- **WHEN** los únicos dos estudiantes de un curso no comparten ningún bloque
- **THEN** ninguno de los dos tiene grupo en ese curso

#### Scenario: Máximo cuatro
- **WHEN** seis estudiantes de un curso comparten el mismo bloque
- **THEN** se forman dos grupos: uno de cuatro y uno de dos

#### Scenario: Sin disponibilidad
- **WHEN** un estudiante de la lista del curso no ha indicado su disponibilidad
- **THEN** no está en ningún grupo de ese curso

### Requirement: Colocar a un estudiante sin reacomodar a los demás
Cuando un estudiante sin grupo en un curso entra a la lista de ese curso o guarda su
disponibilidad, el sistema MUST colocarlo así: primero en el grupo más antiguo del curso con menos
de cuatro integrantes con el que todos compartan al menos un bloque; si no hay, en un grupo nuevo
con compañeros sin grupo del mismo curso que compartan con él al menos un bloque (en orden por
nombre, hasta cuatro); si tampoco hay, queda sin grupo. Colocar a un estudiante MUST NOT mover a
ningún integrante de otro grupo.

#### Scenario: Entra a un grupo con lugar
- **WHEN** existe un grupo de tres que coincide el lunes en la tarde y un cuarto estudiante con ese bloque se inscribe en el curso
- **THEN** el cuarto estudiante entra a ese grupo y los otros tres siguen juntos

#### Scenario: El grupo está lleno
- **WHEN** el único grupo que coincide con el estudiante ya tiene cuatro integrantes
- **THEN** ese grupo no cambia y el estudiante forma un grupo nuevo con compañeros sin grupo, o queda sin grupo

#### Scenario: Se inscribe antes de indicar su disponibilidad
- **WHEN** un estudiante se inscribe sin disponibilidad y después la guarda
- **THEN** queda sin grupo al inscribirse y se le coloca al guardar su disponibilidad

### Requirement: Los grupos conservan a las mismas personas
El sistema MUST guardar los grupos y MUST NOT reacomodarlos para mejorar el reparto. Un integrante
sale de su grupo solo si da de baja el curso o si su nueva disponibilidad ya no comparte un bloque
con el resto del grupo. Si un grupo se queda con un solo integrante, el sistema MUST disolverlo e
intentar colocar a esa persona. Un cambio de nombre MUST NOT cambiar ningún grupo. Procesar el
mismo evento de integración más de una vez MUST dejar los grupos sin cambios.

#### Scenario: Cambia su disponibilidad y sigue coincidiendo
- **WHEN** un integrante cambia su disponibilidad y todavía comparte un bloque con el resto de su grupo
- **THEN** sigue en el mismo grupo con las mismas personas

#### Scenario: Cambia su disponibilidad y deja de coincidir
- **WHEN** un integrante cambia su disponibilidad y ya no comparte ningún bloque con el resto de su grupo
- **THEN** sale de ese grupo y se intenta colocarlo de nuevo
- **AND** los demás integrantes siguen juntos si quedan dos o más

#### Scenario: Da de baja el curso
- **WHEN** llega un `StudentDroppedCourseIntegrationEvent` de un integrante de un grupo de tres
- **THEN** el grupo sigue con los otros dos

#### Scenario: El grupo se queda con uno
- **WHEN** un integrante de un grupo de dos da de baja el curso
- **THEN** el grupo se disuelve y se intenta colocar al estudiante que quedó

#### Scenario: Cambio de nombre
- **WHEN** llega un `StudentRenamedIntegrationEvent` de un integrante
- **THEN** los grupos no cambian y el nombre nuevo aparece en Mis grupos

#### Scenario: Reentrega
- **WHEN** el mismo `StudentEnrolledInCourseIntegrationEvent` se entrega dos veces
- **THEN** el estudiante aparece en un solo grupo y ningún otro grupo cambia

### Requirement: Consultar mis grupos
El sistema MUST devolver, para un estudiante, un elemento por cada curso en cuya lista aparece,
ordenados por código del curso, con los demás integrantes de su grupo y los bloques en común.

#### Scenario: Estudiante con grupo
- **WHEN** se llama a `GET /api/v1/matching/students/{id}/groups`
- **THEN** la respuesta es `200` con una lista de `courseCode`, `members` (`studentId`, `displayName`, ordenados por nombre) y `commonBlocks` (`day`, `slot`)
- **AND** el propio estudiante no aparece en `members`

#### Scenario: Curso sin grupo
- **WHEN** el estudiante está en la lista de un curso pero no tiene grupo en él
- **THEN** ese curso aparece con `members: []` y `commonBlocks: []`

#### Scenario: Estudiante sin cursos
- **WHEN** el estudiante no aparece en ninguna lista del curso, o el id no corresponde a nadie conocido
- **THEN** la respuesta es `200` con una lista vacía

### Requirement: La página Mis grupos
El frontend MUST ofrecer una página "Mis grupos" al estudiante que entró, con un enlace en el
encabezado. Por cada curso MUST mostrar a los integrantes de su grupo y los bloques en común, o
un aviso de que todavía no hay grupo.

#### Scenario: Con grupos
- **WHEN** el estudiante abre Mis grupos y tiene grupo en un curso
- **THEN** ve el código del curso, los nombres de sus compañeros de grupo y los bloques en común en español (por ejemplo, "Lunes · Tarde")

#### Scenario: Sin disponibilidad
- **WHEN** el estudiante no ha indicado su disponibilidad
- **THEN** la página le avisa que la indique y lo lleva a Mis cursos para hacerlo

#### Scenario: Sin cursos
- **WHEN** el estudiante no está inscrito en ningún curso
- **THEN** la página muestra un estado vacío que lo invita a inscribirse en Mis cursos
