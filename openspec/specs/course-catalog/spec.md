# course-catalog Specification

## Purpose
The catalog is the list of courses a student can enroll in. It is read-only in v1: seeded once,
never edited through the API. Owned by student-service (module Students).

## Requirements

### Requirement: Course codes have one format
The system SHALL identify a course by a code of three or four letters, a hyphen and three digits
(for example `COMP-248`), always stored and returned in upper case.

#### Scenario: Normalization
- **WHEN** a course code is supplied as `comp-248` or with surrounding spaces
- **THEN** it is treated as `COMP-248`

#### Scenario: Malformed code
- **WHEN** a course code does not match the format (for example `COMP248` or `banana`)
- **THEN** the request is rejected with `400` and `code: course.invalid_code`

### Requirement: List the catalog
The system SHALL list every course with its code, title, department and the number of students
currently enrolled, ordered by course code.

#### Scenario: Catalog with counts
- **WHEN** `GET /api/v1/courses` is called
- **THEN** each item carries `code`, `title`, `department` and `enrolledCount`
- **AND** `enrolledCount` reflects enrollments and drops made so far

### Requirement: The catalog is seeded
In the Development environment the system SHALL seed the catalog (and a set of demo students)
into an empty database at startup, and SHALL NOT expose any endpoint that edits the catalog.

#### Scenario: First start
- **WHEN** student-service starts in Development against an empty database
- **THEN** the catalog and the demo students exist afterwards
- **AND** the demo students were enrolled through the same commands the API uses, so their enrollment events were published
