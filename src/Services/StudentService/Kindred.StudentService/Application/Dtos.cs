using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application;

// What the API returns. Entities never leave the service — only these.

public sealed record EnrollmentDto(string CourseCode, DateTimeOffset EnrolledAt);

public sealed record StudentDto(
    Guid Id, string DisplayName, string Email, string Program, IReadOnlyList<EnrollmentDto> Enrollments);

public sealed record StudentSummaryDto(Guid Id, string DisplayName, string Program, int CourseCount);

public sealed record CourseDto(string Code, string Title, string Department, int EnrolledCount);

public sealed record ClassmateDto(
    Guid StudentId, string DisplayName, string Program, IReadOnlyList<string> SharedCourses, int SharedCount);

public static class StudentMappings
{
    public static StudentDto ToDto(this Student student) => new(
        student.Id,
        student.DisplayName,
        student.Email,
        student.Program,
        student.Enrollments
            .OrderBy(e => e.CourseCode.Value, StringComparer.Ordinal)
            .Select(e => new EnrollmentDto(e.CourseCode.Value, e.EnrolledAt))
            .ToList());

    public static StudentSummaryDto ToSummaryDto(this Student student) =>
        new(student.Id, student.DisplayName, student.Program, student.Enrollments.Count);
}
