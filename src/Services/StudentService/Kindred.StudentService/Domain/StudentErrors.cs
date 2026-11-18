using Kindred.SharedKernel.Domain;

namespace Kindred.StudentService.Domain;

/// <summary>Every expected failure of the Students module, in one place.</summary>
public static class StudentErrors
{
    public static Error InvalidCourseCode(string? raw) =>
        Error.Validation("course.invalid_code", $"'{raw}' no es un código de curso. Usa algo como COMP-248.");

    public static Error CourseNotFound(CourseCode code) =>
        Error.NotFound("course.not_found", $"El curso {code} no está en el catálogo.");

    public static Error StudentNotFound(Guid id) =>
        Error.NotFound("student.not_found", $"No existe el estudiante {id}.");

    public static readonly Error DisplayNameRequired =
        Error.Validation("student.display_name_required", "Escribe tu nombre.");

    public static readonly Error ProgramRequired =
        Error.Validation("student.program_required", "Escribe tu carrera.");

    public static readonly Error InvalidEmail =
        Error.Validation("student.invalid_email", "Ese correo no parece válido.");

    public static Error EmailTaken(string email) =>
        Error.Conflict("student.email_taken", $"Ya hay un estudiante registrado con el correo {email}.");

    public static Error AlreadyEnrolled(CourseCode code) =>
        Error.Conflict("enrollment.already_enrolled", $"Ya estás inscrito en {code}.");

    public static readonly Error CourseLimitReached =
        Error.Validation("enrollment.course_limit_reached", $"Puedes tomar como máximo {Student.MaxCourses} cursos.");

    public static Error NotEnrolled(CourseCode code) =>
        Error.NotFound("enrollment.not_enrolled", $"No estás inscrito en {code}.");
}
