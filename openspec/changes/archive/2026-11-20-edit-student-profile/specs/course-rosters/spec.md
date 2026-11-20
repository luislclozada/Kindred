## ADDED Requirements

### Requirement: La lista del curso refleja los cambios de nombre
Al recibir un `StudentRenamedIntegrationEvent`, matching-service MUST actualizar el nombre del
estudiante en todas las listas del curso donde aparece. El manejo MUST ser idempotente y MUST
NOT agregar al estudiante a ninguna lista.

#### Scenario: El estudiante cambia su nombre
- **WHEN** llega un `StudentRenamedIntegrationEvent` de un estudiante inscrito en dos cursos
- **THEN** `GET /api/v1/matching/courses/{courseCode}/roster` de ambos cursos muestra el `displayName` nuevo

#### Scenario: Evento repetido
- **WHEN** el mismo `StudentRenamedIntegrationEvent` se entrega dos veces
- **THEN** las listas quedan igual que tras la primera entrega

#### Scenario: Estudiante sin cursos
- **WHEN** llega un `StudentRenamedIntegrationEvent` de un estudiante que no está en ninguna lista
- **THEN** ninguna lista cambia
