# course-enrollment Specification

## Purpose
A student enrolls in courses from the catalog and can drop them again. The rules live in the
`Student` aggregate. Every enrollment and drop is announced to the rest of the system as an
integration event. Owned by student-service (module Students).

## Requirements

### Requirement: Enroll in a course
The system SHALL let a student enroll in a course that exists in the catalog and SHALL return the
updated student.

#### Scenario: Successful enrollment
- **WHEN** `POST /api/v1/students/{id}/enrollments` is sent with `{ "courseCode": "COMP-248" }`
- **THEN** the response is `200` with the student, whose enrollments now include `COMP-248`

#### Scenario: Unknown student or course
- **WHEN** the student does not exist, or the course code is well-formed but not in the catalog
- **THEN** the response is `404` with `code: student.not_found` or `code: course.not_found`

### Requirement: No duplicate enrollment
The system SHALL refuse to enroll a student in a course they already take.

#### Scenario: Already enrolled
- **WHEN** a student enrolls in a course they are already enrolled in
- **THEN** the response is `409` with `code: enrollment.already_enrolled`

### Requirement: At most six courses
The system SHALL refuse an enrollment that would give a student more than six courses.

#### Scenario: Seventh course
- **WHEN** a student with six enrollments enrolls in another course
- **THEN** the response is `400` with `code: enrollment.course_limit_reached`

### Requirement: Drop a course
The system SHALL let a student drop a course they are enrolled in and SHALL return the updated
student.

#### Scenario: Successful drop
- **WHEN** `DELETE /api/v1/students/{id}/enrollments/{courseCode}` is called for an enrolled course
- **THEN** the response is `200` with the student, whose enrollments no longer include that course

#### Scenario: Not enrolled
- **WHEN** the student is not enrolled in that course
- **THEN** the response is `404` with `code: enrollment.not_enrolled`

### Requirement: Enrollment changes are published
After an enrollment or a drop has been saved, student-service SHALL publish
`StudentEnrolledInCourseIntegrationEvent` or `StudentDroppedCourseIntegrationEvent` on RabbitMQ.
It SHALL NOT publish anything for a request that failed.

#### Scenario: Enrollment event
- **WHEN** an enrollment succeeds
- **THEN** a `StudentEnrolledInCourseIntegrationEvent` carrying the student id, the student's display name and the course code is published

#### Scenario: Drop event
- **WHEN** a drop succeeds
- **THEN** a `StudentDroppedCourseIntegrationEvent` carrying the student id and the course code is published
