using System.Text.RegularExpressions;
using Kindred.SharedKernel.Domain;

namespace Kindred.StudentService.Domain;

/// <summary>A course code such as COMP-248. Always upper-case; invalid codes cannot exist.</summary>
public sealed partial record CourseCode
{
    private CourseCode(string value) => Value = value;

    public string Value { get; }

    public static Result<CourseCode> Create(string? raw)
    {
        var normalized = raw?.Trim().ToUpperInvariant() ?? "";
        return Format().IsMatch(normalized)
            ? new CourseCode(normalized)
            : StudentErrors.InvalidCourseCode(raw);
    }

    /// <summary>For values that were already validated (the database, the seeded catalog).</summary>
    public static CourseCode From(string value) =>
        Create(value) is { IsSuccess: true } result
            ? result.Value
            : throw new ArgumentException($"'{value}' is not a valid course code.", nameof(value));

    public override string ToString() => Value;

    [GeneratedRegex(@"^[A-Z]{3,4}-\d{3}$")]
    private static partial Regex Format();
}
