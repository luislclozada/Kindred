# classmate-discovery Specification

## Purpose
A student sees who else takes their courses, so they can find people to study with. This is the
core of Kindred v1. Owned by student-service (module Students); shown on the frontend's
Classmates page.

## Requirements

### Requirement: List classmates
The system SHALL return, for a student, every other student who shares at least one course with
them, together with the courses they share.

#### Scenario: Shared courses
- **WHEN** `GET /api/v1/students/{id}/classmates` is called
- **THEN** each item carries `studentId`, `displayName`, `program`, `sharedCourses` (course codes) and `sharedCount`
- **AND** the student themself is never in the list

#### Scenario: No courses yet
- **WHEN** the student is not enrolled in any course
- **THEN** the response is `200` with an empty list

#### Scenario: Unknown student
- **WHEN** the id does not belong to any student
- **THEN** the response is `404` with `code: student.not_found`

### Requirement: Rank by shared courses
The system SHALL order classmates by the number of shared courses, most first, and by display
name when the count is equal.

#### Scenario: Ranking
- **WHEN** one classmate shares three courses and another shares one
- **THEN** the classmate sharing three courses comes first

### Requirement: Classmates page groups by course
The frontend SHALL show classmates grouped under each of the student's courses, with a badge on
each classmate showing how many courses they share in total.

#### Scenario: Grouped view
- **WHEN** a signed-in student opens the Classmates page
- **THEN** there is one group per enrolled course listing the classmates who take it
- **AND** a classmate who shares several courses appears in each of those groups with the same shared count
