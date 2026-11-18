using Kindred.SharedKernel.Domain;

namespace Kindred.StudentService.Domain;

/// <summary>Every expected failure of the Students module, in one place.</summary>
public static class StudentErrors
{
    public static Error InvalidCourseCode(string? raw) =>
        Error.Validation("course.invalid_code", $"'{raw}' is not a course code. Expected something like COMP-248.");

    public static Error CourseNotFound(CourseCode code) =>
        Error.NotFound("course.not_found", $"Course {code} is not in the catalog.");

    public static Error StudentNotFound(Guid id) =>
        Error.NotFound("student.not_found", $"Student {id} does not exist.");

    public static readonly Error DisplayNameRequired =
        Error.Validation("student.display_name_required", "A display name is required.");

    public static readonly Error ProgramRequired =
        Error.Validation("student.program_required", "A program is required.");

    public static readonly Error InvalidEmail =
        Error.Validation("student.invalid_email", "That does not look like an email address.");

    public static Error EmailTaken(string email) =>
        Error.Conflict("student.email_taken", $"A student with the email {email} is already registered.");

    public static Error AlreadyEnrolled(CourseCode code) =>
        Error.Conflict("enrollment.already_enrolled", $"Already enrolled in {code}.");

    public static readonly Error CourseLimitReached =
        Error.Validation("enrollment.course_limit_reached", $"A student can take at most {Student.MaxCourses} courses.");

    public static Error NotEnrolled(CourseCode code) =>
        Error.NotFound("enrollment.not_enrolled", $"Not enrolled in {code}.");
}
