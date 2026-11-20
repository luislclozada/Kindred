## Why

Kindred le muestra a un estudiante quién más toma sus cursos, pero no con quién puede estudiar de
verdad: dos compañeros del mismo curso no sirven como grupo de estudio si nunca están libres a la
misma hora. Hoy no hay forma de indicar la disponibilidad, y la formación de grupos de estudio,
anunciada desde v1 como "el siguiente cambio", sigue sin existir.

**Revisión del grafo de conocimiento** (`q query "formación de grupos de estudio por curso y
disponibilidad semanal del estudiante" --kind existence`, `q detect-duplicates`): no se encontró
cobertura existente. No hay spec, cambio activo ni código de disponibilidad o de grupos. Lo más
cercano:

- La capacidad `study-group-matching` (`knowledge-graph/capabilities.yaml@5c3ff164`) ya anuncia
  este trabajo ("después, formar grupos de estudio por curso y disponibilidad") y ya tiene el
  alias `availability-*`. Este cambio **cumple** esa capacidad; no crea una nueva.
- La spec `course-rosters` (`openspec/specs/course-rosters/spec.md@5c3ff164`) es la base sobre la
  que se forman los grupos: matching-service ya sabe quién toma cada curso.
- La spec `classmate-discovery` (`openspec/specs/classmate-discovery/spec.md@5c3ff164`) no se
  traslapa: lista compañeros por cursos en común, sin disponibilidad y sin formar grupos.
- `docs/features/classmate-discovery/README.md@5c3ff164` y `docs/DEMO.md@5c3ff164` describen este
  mismo cambio como el siguiente paso.

`detect-duplicates`: sin candidatos relacionados (el único resultado es la pareja ya conocida
`change:edit-student-profile` ↔ `feature:edit-student-profile`).

## What Changes

- **matching-service** (dominio, aplicación, datos, API) es dueño de la disponibilidad:
  - Un estudiante guarda su disponibilidad semanal como bloques de día × franja (mañana / tarde /
    noche) con `PUT /api/v1/matching/students/{id}/availability` y la consulta con `GET` en la
    misma ruta. En v1 se acepta cualquier id de estudiante, sin validarlo.
  - Los grupos de estudio **se guardan y conservan a las mismas personas**: compañeros del mismo
    curso que comparten al menos un bloque, de dos a cuatro por grupo, un grupo por estudiante por
    curso. Un estudiante nuevo entra a un grupo con lugar o forma uno con compañeros sin grupo,
    sin reacomodar a nadie; solo sale si da de baja el curso o si su disponibilidad deja de
    coincidir. Quien no coincide con nadie queda sin grupo.
  - Los handlers de `StudentEnrolledInCourseIntegrationEvent` y `StudentDroppedCourseIntegrationEvent`,
    que ya existen, ahora también colocan o sacan al estudiante de su grupo; guardar la
    disponibilidad lo recoloca en sus cursos.
  - `GET /api/v1/matching/students/{id}/groups` devuelve el grupo de estudio del estudiante en cada
    curso donde está en la lista del curso.
  - Tablas nuevas de disponibilidad y de grupos, y su migración de EF.
- **Frontend**: un editor de disponibilidad (cuadrícula de 7 días × 3 franjas) en **Mis cursos**,
  y una página nueva **Mis grupos** con su enlace en el encabezado.
- **student-service**: sin cambios.
- **Eventos de integración**: ninguno nuevo ni modificado. matching-service sigue consumiendo los
  tres que ya consume; cambia lo que hacen sus handlers, no los contratos.
- **Cambio en paralelo `course-waitlist`**: compatible. Publica el mismo
  `StudentEnrolledInCourseIntegrationEvent` cuando alguien sale de la lista de espera, y matching
  lo coloca en un grupo como a cualquier inscripción. Los dos cambios tocan Mis cursos: este solo
  agrega el componente del editor al final (ver design.md §7).
- **Gateway**: sin rutas nuevas; `/api/v1/matching/{**rest}` ya cubre los tres endpoints.
- **Grafo de conocimiento**: sin puente nuevo entre módulos. Se actualiza la descripción de la
  capacidad `study-group-matching` y se registran los nombres del cambio y de sus specs.

## Capabilities

### New Capabilities

- `study-availability`: la disponibilidad semanal del estudiante en bloques de día × franja, cómo
  se guarda y se consulta, y el editor en Mis cursos.
- `study-groups`: la formación de grupos de estudio por curso y disponibilidad en común, cómo se
  conservan, el endpoint que los devuelve y la página Mis grupos.

### Modified Capabilities

_(ninguna; `course-rosters` se usa tal como está)_

## Impact

- `src/Services/MatchingService/**`: aggregate de disponibilidad, value object de bloque,
  `StudyGroup` dentro de `CourseRoster` con sus reglas, los handlers de inscripción y de baja,
  comandos y consultas, configuración de EF, migración y endpoints nuevos.
- `tests/`: pruebas unitarias de matching-service (proyecto nuevo si todavía no existe).
- `apps/frontend/src/app/**`: `matching.api.ts`, `core/models/matching.models.ts` (nuevo), facades
  nuevos, el componente del editor, una línea en la página Mis cursos, la página nueva `groups`,
  la ruta y el encabezado. No se toca `api.models.ts` ni `courses.facade.ts` (los edita
  `course-waitlist`).
- `docs/FEATURES.md` y `docs/features/study-groups/README.md`.
- `knowledge-graph/capabilities.yaml`.
- Fuera de alcance: notificaciones, chat de grupo, horas exactas o zonas horarias, validar que el
  estudiante exista, y datos de ejemplo de disponibilidad.
