using Kindred.SharedKernel.Domain;

namespace Kindred.StudentService.Domain;

/// <summary>A course in the catalog. The catalog is read-only in v1: it is seeded, never edited.</summary>
public sealed class Course : AggregateRoot<CourseCode>
{
    private Course() { } // EF Core

    private Course(CourseCode code, string title, string department)
    {
        Id = code;
        Title = title;
        Department = department;
    }

    public CourseCode Code => Id;
    public string Title { get; private set; } = "";
    public string Department { get; private set; } = "";

    public static Course Create(CourseCode code, string title, string department) =>
        new(code, title.Trim(), department.Trim());
}
