# api-gateway Specification

## Purpose
El gateway es el único punto de entrada público. El frontend solo habla con él; el gateway
reenvía cada solicitud al servicio dueño de la ruta. Las rutas son configuración (YARP), no
código.

## Requirements

### Requirement: El gateway enruta por prefijo de ruta
El gateway MUST reenviar `/api/v1/students/**` y `/api/v1/courses/**` a student-service y
`/api/v1/matching/**` a matching-service, sin modificar la ruta ni la respuesta.

#### Scenario: Ruta de estudiantes
- **WHEN** se envía `GET /api/v1/courses` al gateway
- **THEN** student-service la responde y el gateway devuelve esa respuesta tal cual

#### Scenario: Ruta de matching
- **WHEN** se envía `GET /api/v1/matching/courses/COMP-248/roster` al gateway
- **THEN** matching-service la responde

### Requirement: El id del cluster nombra al servicio destino
Cada id de cluster de YARP MUST ser igual al nombre kebab-case del servicio al que apunta
(`student-service`, `matching-service`), para que la tabla de rutas se lea como "el gateway
llama al servicio".

#### Scenario: Tabla de rutas legible
- **WHEN** se lee la configuración del gateway
- **THEN** el `ClusterId` de cada ruta es el nombre de un servicio que existe

### Requirement: El gateway permite el origen del frontend
El gateway MUST responder las solicitudes CORS que vienen del origen de desarrollo del frontend,
`http://localhost:4200`.

#### Scenario: Preflight
- **WHEN** llega un preflight `OPTIONS` con `Origin: http://localhost:4200`
- **THEN** la respuesta permite ese origen
