using Kindred.SharedKernel.Domain;

namespace Kindred.MatchingService.Domain;

/// <summary>
/// Who takes a course, as matching-service knows it. Built only from student-service's enrollment
/// events — matching never asks student-service anything over HTTP. Both operations are
/// idempotent, so a redelivered event changes nothing.
/// </summary>
public sealed class CourseRoster : AggregateRoot<string>
{
    private readonly List<RosterMember> _members = [];

    private CourseRoster() { } // EF Core

    private CourseRoster(string courseCode) => Id = courseCode;

    public string CourseCode => Id;
    public IReadOnlyList<RosterMember> Members => _members;

    public static CourseRoster For(string courseCode) => new(Normalize(courseCode));

    public void Add(Guid studentId, string studentName)
    {
        if (_members.Any(m => m.StudentId == studentId)) return;
        _members.Add(new RosterMember(studentId, studentName));
    }

    public void Remove(Guid studentId) => _members.RemoveAll(m => m.StudentId == studentId);

    /// <summary>Updates the display name of an existing member; never adds one.</summary>
    public void RenameMember(Guid studentId, string studentName) =>
        _members.FirstOrDefault(m => m.StudentId == studentId)?.Rename(studentName);

    public static string Normalize(string courseCode) => courseCode.Trim().ToUpperInvariant();
}

public sealed class RosterMember
{
    private RosterMember() { } // EF Core

    internal RosterMember(Guid studentId, string studentName)
    {
        StudentId = studentId;
        StudentName = studentName;
    }

    public Guid StudentId { get; private set; }
    public string StudentName { get; private set; } = "";

    internal void Rename(string studentName)
    {
        if (StudentName == studentName) return;
        StudentName = studentName;
    }
}

public interface ICourseRosterRepository
{
    Task<CourseRoster?> GetAsync(string courseCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<CourseRoster>> ListContainingStudentAsync(Guid studentId, CancellationToken cancellationToken);
    void Add(CourseRoster roster);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
