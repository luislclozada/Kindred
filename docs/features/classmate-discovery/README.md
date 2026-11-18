# Descubrir compañeros

Kindred v1. Un estudiante elige quién es, agrega sus cursos y ve a las personas que toman los
mismos: entre más cursos en común tengan dos estudiantes, más arriba aparecen el uno para el otro.

## Qué puede hacer un estudiante

1. **Entrar como** un estudiante que ya existe, o registrarse (nombre, correo, carrera). No hay
   contraseña: v1 no tiene autenticación.
2. **Mis cursos**: recorre el catálogo, inscríbete y da de baja cursos. Máximo seis cursos.
3. **Compañeros**: un grupo por curso con quién más lo toma, y una insignia que muestra cuántos
   cursos en común tienes en total con cada compañero.

## Cómo funciona

| Pieza | Dónde | Qué hace |
|---|---|---|
| student-service | `src/Services/StudentService/` | Estudiantes, el catálogo, las inscripciones, la consulta de compañeros |
| matching-service | `src/Services/MatchingService/` | Mantiene una lista por curso, construida solo con eventos de inscripción |
| gateway | `src/Gateway/Kindred.Gateway/` | La única dirección a la que llama el frontend |
| frontend | `apps/frontend/` | Entrar como, Mis cursos, Compañeros |

Inscribirse o dar de baja un curso cambia el agregado `Student`, se guarda y luego se anuncia como
`StudentEnrolledInCourseIntegrationEvent` / `StudentDroppedCourseIntegrationEvent` en RabbitMQ.
matching-service escucha y actualiza sus listas de curso. Los dos servicios nunca se llaman
entre sí por HTTP.

## Specs

`student-profiles`, `course-catalog`, `course-enrollment`, `classmate-discovery`,
`course-rosters` y `api-gateway` en `openspec/specs/`.

## Fuera de v1

Formar grupos de estudio. matching-service ya sabe quién toma qué; agrupar estudiantes por curso
y por disponibilidad que coincida es el siguiente cambio.
