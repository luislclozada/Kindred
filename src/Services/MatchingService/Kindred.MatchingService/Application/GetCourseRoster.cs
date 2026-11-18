using Kindred.MatchingService.Domain;
using Kindred.SharedKernel.Mediator;

namespace Kindred.MatchingService.Application;

public sealed record RosterStudentDto(Guid StudentId, string DisplayName);

public sealed record CourseRosterDto(string CourseCode, IReadOnlyList<RosterStudentDto> Students);

/// <summary>The roster of one course. A course nobody has enrolled in yet has an empty roster.</summary>
public sealed record GetCourseRosterQuery(string CourseCode) : IQuery<CourseRosterDto>;

public sealed class GetCourseRosterHandler(ICourseRosterRepository rosters)
    : IQueryHandler<GetCourseRosterQuery, CourseRosterDto>
{
    public async Task<CourseRosterDto> Handle(GetCourseRosterQuery query, CancellationToken cancellationToken)
    {
        var courseCode = CourseRoster.Normalize(query.CourseCode);
        var roster = await rosters.GetAsync(courseCode, cancellationToken);

        var students = (roster?.Members ?? [])
            .OrderBy(m => m.StudentName, StringComparer.OrdinalIgnoreCase)
            .Select(m => new RosterStudentDto(m.StudentId, m.StudentName))
            .ToList();
        return new CourseRosterDto(courseCode, students);
    }
}
