## Purpose

La disponibilidad del estudiante: los bloques de la semana (día × mañana / tarde / noche) en los
que puede estudiar, para que la formación de grupos de estudio junte a quienes coinciden. La
administra matching-service (módulo Matching) y se edita en Mis cursos.

## ADDED Requirements

### Requirement: Guardar la disponibilidad
El sistema MUST permitir que un estudiante reemplace toda su disponibilidad con un conjunto de
bloques. Cada bloque es un día (`MONDAY` a `SUNDAY`) y una franja (`MORNING`, `AFTERNOON`,
`EVENING`). Un conjunto vacío MUST ser válido y deja al estudiante sin disponibilidad. En v1 el
sistema MUST aceptar cualquier id de estudiante sin validar que exista.

#### Scenario: Guardado exitoso
- **WHEN** se envía `PUT /api/v1/matching/students/{id}/availability` con `blocks` (`day`, `slot`)
- **THEN** la respuesta es `200` con `studentId` y `blocks` tal como quedaron guardados
- **AND** los bloques repetidos se guardan una sola vez
- **AND** los bloques vienen ordenados por día (de lunes a domingo) y luego por franja (mañana, tarde, noche)

#### Scenario: Reemplaza la disponibilidad anterior
- **WHEN** un estudiante que tenía disponibilidad el lunes en la mañana guarda solo el martes en la tarde
- **THEN** su disponibilidad es únicamente el martes en la tarde

#### Scenario: Borrar la disponibilidad
- **WHEN** se envía `PUT` con `blocks: []`
- **THEN** la respuesta es `200` con `blocks: []`

#### Scenario: Bloque inválido
- **WHEN** un bloque trae un `day` o un `slot` que no existe
- **THEN** la respuesta es `400` con un `ApiError` cuyo `code` es `availability.invalid_block`
- **AND** la disponibilidad guardada no cambia

### Requirement: Consultar la disponibilidad
El sistema MUST devolver la disponibilidad guardada de un estudiante, con el mismo orden que al
guardarla.

#### Scenario: Estudiante con disponibilidad
- **WHEN** se llama a `GET /api/v1/matching/students/{id}/availability`
- **THEN** la respuesta es `200` con `studentId` y sus `blocks`

#### Scenario: Estudiante que nunca la ha indicado
- **WHEN** el estudiante nunca guardó su disponibilidad, o el id no corresponde a nadie conocido
- **THEN** la respuesta es `200` con `blocks: []`

### Requirement: El editor de disponibilidad
El frontend MUST ofrecer al estudiante que entró un editor de disponibilidad: una cuadrícula de
siete días por tres franjas donde cada casilla se marca o se desmarca, con un botón para guardar.
El editor MUST aparecer en Mis cursos y MUST mostrar la disponibilidad guardada. Guardar la
disponibilidad MUST recolocar al estudiante en los grupos de estudio de sus cursos (ver la spec
`study-groups`).

#### Scenario: Marcar y guardar
- **WHEN** el estudiante marca el miércoles en la noche y guarda
- **THEN** la casilla queda marcada después de guardar y al volver a abrir la página

#### Scenario: El guardado falla
- **WHEN** el servidor responde con un `ApiError`
- **THEN** el editor muestra el mensaje del `ApiError` y conserva las casillas que marcó el estudiante
