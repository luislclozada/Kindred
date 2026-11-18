# api-gateway Specification

## Purpose
The gateway is the single public entry point. The frontend talks only to it; it forwards each
request to the service that owns the path. Routes are configuration (YARP), not code.

## Requirements

### Requirement: Route by path prefix
The gateway SHALL forward `/api/v1/students/**` and `/api/v1/courses/**` to student-service and
`/api/v1/matching/**` to matching-service, leaving the path and the response untouched.

#### Scenario: Student route
- **WHEN** `GET /api/v1/courses` is sent to the gateway
- **THEN** student-service answers it and the gateway returns that response as is

#### Scenario: Matching route
- **WHEN** `GET /api/v1/matching/courses/COMP-248/roster` is sent to the gateway
- **THEN** matching-service answers it

### Requirement: Cluster ids name the target service
Each YARP cluster id SHALL equal the kebab-case name of the service it targets
(`student-service`, `matching-service`), so the route table can be read as "gateway calls service".

#### Scenario: Readable route table
- **WHEN** the gateway configuration is read
- **THEN** every route's `ClusterId` is the name of an existing service

### Requirement: Allow the frontend origin
The gateway SHALL answer CORS requests from the frontend dev origin `http://localhost:4200`.

#### Scenario: Preflight
- **WHEN** an `OPTIONS` preflight arrives with `Origin: http://localhost:4200`
- **THEN** the response allows that origin
