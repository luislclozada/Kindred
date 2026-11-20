using Kindred.SharedKernel.Domain;

namespace Kindred.StudentService.Domain;

/// <summary>
/// A student and the courses they take. Every enrollment rule lives here: no duplicates, at most
/// <see cref="MaxCourses"/> courses, and only an enrolled course can be dropped.
/// </summary>
public sealed class Student : AggregateRoot<Guid>
{
    public const int MaxCourses = 6;

    private readonly List<Enrollment> _enrollments = [];

    private Student() { } // EF Core

    private Student(Guid id, string displayName, string email, string program, DateTimeOffset registeredAt)
    {
        Id = id;
        DisplayName = displayName;
        Email = email;
        Program = program;
        RegisteredAt = registeredAt;
    }

    public string DisplayName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Program { get; private set; } = "";
    public DateTimeOffset RegisteredAt { get; private set; }
    public IReadOnlyList<Enrollment> Enrollments => _enrollments;

    public static Result<Student> Register(string? displayName, string? email, string? program, DateTimeOffset now)
    {
        var invalidProfile = ValidateProfile(displayName, program);
        if (invalidProfile is not null) return invalidProfile;

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null) return StudentErrors.InvalidEmail;

        return new Student(Guid.NewGuid(), displayName!.Trim(), normalizedEmail, program!.Trim(), now);
    }

    /// <summary>Replaces name and program. Email and enrollments are untouched; only a real rename raises an event.</summary>
    public Result UpdateProfile(string? displayName, string? program, DateTimeOffset now)
    {
        var invalidProfile = ValidateProfile(displayName, program);
        if (invalidProfile is not null) return Result.Failure(invalidProfile);

        var newName = displayName!.Trim();
        var renamed = !string.Equals(newName, DisplayName, StringComparison.Ordinal);

        DisplayName = newName;
        Program = program!.Trim();

        if (renamed) Raise(new StudentRenamed(Id, newName, now));
        return Result.Success();
    }

    private static Error? ValidateProfile(string? displayName, string? program)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return StudentErrors.DisplayNameRequired;
        if (string.IsNullOrWhiteSpace(program)) return StudentErrors.ProgramRequired;
        return null;
    }

    public Result Enroll(CourseCode course, DateTimeOffset now)
    {
        if (IsEnrolledIn(course)) return Result.Failure(StudentErrors.AlreadyEnrolled(course));
        if (_enrollments.Count >= MaxCourses) return Result.Failure(StudentErrors.CourseLimitReached);

        _enrollments.Add(new Enrollment(course, now));
        Raise(new StudentEnrolledInCourse(Id, DisplayName, course, now));
        return Result.Success();
    }

    public Result Drop(CourseCode course, DateTimeOffset now)
    {
        var enrollment = _enrollments.FirstOrDefault(e => e.CourseCode == course);
        if (enrollment is null) return Result.Failure(StudentErrors.NotEnrolled(course));

        _enrollments.Remove(enrollment);
        Raise(new StudentDroppedCourse(Id, course, now));
        return Result.Success();
    }

    public bool IsEnrolledIn(CourseCode course) => _enrollments.Any(e => e.CourseCode == course);

    /// <summary>Lower-cased, trimmed email, or null when it is not shaped like one.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var value = email?.Trim().ToLowerInvariant() ?? "";
        var at = value.IndexOf('@');
        var looksValid = at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1
                         && !value.EndsWith('.') && !value.Any(char.IsWhiteSpace);
        return looksValid ? value : null;
    }
}

/// <summary>One course a student takes. Lives and dies with its <see cref="Student"/>.</summary>
public sealed class Enrollment
{
    private Enrollment() { } // EF Core

    internal Enrollment(CourseCode courseCode, DateTimeOffset enrolledAt)
    {
        CourseCode = courseCode;
        EnrolledAt = enrolledAt;
    }

    public CourseCode CourseCode { get; private set; } = null!;
    public DateTimeOffset EnrolledAt { get; private set; }
}
