# Classmate discovery

Kindred v1. A student picks who they are, adds their courses, and sees the people who take the
same ones — the more courses two students share, the higher they rank for each other.

## What a student can do

1. **Sign in as** an existing student, or register (display name, email, program). There is no
   password: v1 has no authentication.
2. **My courses** — browse the catalog, enroll and drop. At most six courses.
3. **Classmates** — one group per course, listing who else takes it, with a badge showing how
   many courses each classmate shares in total.

## How it works

| Piece | Where | Does |
|---|---|---|
| student-service | `src/Services/StudentService/` | Students, the catalog, enrollments, the classmates query |
| matching-service | `src/Services/MatchingService/` | Keeps a roster per course, built only from enrollment events |
| gateway | `src/Gateway/Kindred.Gateway/` | The one address the frontend calls |
| frontend | `apps/frontend/` | Sign in as, My courses, Classmates |

Enrolling or dropping changes the `Student` aggregate, is saved, and is then announced as
`StudentEnrolledInCourseIntegrationEvent` / `StudentDroppedCourseIntegrationEvent` on RabbitMQ.
matching-service listens and updates its rosters. The two services never call each other over
HTTP.

## Specs

`student-profiles`, `course-catalog`, `course-enrollment`, `classmate-discovery`,
`course-rosters` and `api-gateway` under `openspec/specs/`.

## Not in v1

Forming study groups. matching-service already knows who takes what; grouping students by
course and overlapping availability is the next change.
