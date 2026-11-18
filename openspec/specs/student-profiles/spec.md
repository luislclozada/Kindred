# student-profiles Specification

## Purpose
Students exist in Kindred as a profile: a display name, a unique email and a program. There is
no authentication in v1 — a profile is created by registering and selected with a "sign in as"
picker. Owned by student-service (module Students).

## Requirements

### Requirement: Register a student
The system SHALL let anyone register a student with a display name, an email and a program, and
SHALL return the new student with an empty list of enrollments.

#### Scenario: Successful registration
- **WHEN** `POST /api/v1/students` is sent with a display name, a valid email and a program
- **THEN** the response is `201 Created` with the student (`id`, `displayName`, `email`, `program`, `enrollments: []`)
- **AND** the email is stored trimmed and lower-cased

#### Scenario: Missing details
- **WHEN** the display name or the program is blank, or the email is not shaped like an email
- **THEN** the response is `400` with an `ApiError` whose `code` names the problem (`student.display_name_required`, `student.program_required`, `student.invalid_email`)

### Requirement: Emails are unique
The system SHALL refuse to register a second student with an email that is already registered,
compared case-insensitively.

#### Scenario: Duplicate email
- **WHEN** a registration uses an email that another student already has
- **THEN** the response is `409` with `code: student.email_taken`
- **AND** no student is created

### Requirement: List students
The system SHALL list every student as a summary, ordered by display name, so the UI can offer a
"sign in as" picker.

#### Scenario: Picker data
- **WHEN** `GET /api/v1/students` is called
- **THEN** each item carries `id`, `displayName`, `program` and `courseCount`

### Requirement: Get a student
The system SHALL return one student with their enrollments.

#### Scenario: Known student
- **WHEN** `GET /api/v1/students/{id}` is called for an existing student
- **THEN** the response is `200` with the student and their enrollments (`courseCode`, `enrolledAt`), ordered by course code

#### Scenario: Unknown student
- **WHEN** the id does not belong to any student
- **THEN** the response is `404` with `code: student.not_found`
