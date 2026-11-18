# course-rosters Specification

## Purpose
matching-service keeps its own picture of who takes each course — the course roster — so that
study group matching can be built on it without ever asking student-service. Owned by
matching-service (module Matching).

## Requirements

### Requirement: Rosters are built from enrollment events
matching-service SHALL maintain one roster per course from student-service's integration events
and SHALL NOT call student-service over HTTP.

#### Scenario: Student enrolls
- **WHEN** a `StudentEnrolledInCourseIntegrationEvent` is received
- **THEN** the student (id and display name) is on that course's roster

#### Scenario: Student drops
- **WHEN** a `StudentDroppedCourseIntegrationEvent` is received
- **THEN** the student is no longer on that course's roster

### Requirement: Event handling is idempotent
Handling the same event more than once SHALL leave the roster unchanged.

#### Scenario: Redelivery
- **WHEN** the same enrollment event is delivered twice
- **THEN** the student appears on the roster once

### Requirement: Read a course roster
The system SHALL return the roster of a course, ordered by display name.

#### Scenario: Roster with students
- **WHEN** `GET /api/v1/matching/courses/{courseCode}/roster` is called
- **THEN** the response is `200` with `courseCode` (upper case) and `students` (`studentId`, `displayName`)

#### Scenario: Course nobody takes
- **WHEN** no student has enrolled in that course
- **THEN** the response is `200` with an empty `students` list
