using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Queries;

/// <summary>The course catalog, each course with how many students take it.</summary>
public sealed record ListCoursesQuery : IQuery<IReadOnlyList<CourseDto>>;

public sealed class ListCoursesHandler(ICourseRepository courses, IStudentRepository students)
    : IQueryHandler<ListCoursesQuery, IReadOnlyList<CourseDto>>
{
    public async Task<IReadOnlyList<CourseDto>> Handle(ListCoursesQuery query, CancellationToken cancellationToken)
    {
        var catalog = await courses.ListAsync(cancellationToken);
        var enrolledCounts = await students.CountEnrollmentsByCourseAsync(cancellationToken);

        return catalog
            .OrderBy(c => c.Code.Value, StringComparer.Ordinal)
            .Select(c => new CourseDto(c.Code.Value, c.Title, c.Department, enrolledCounts.GetValueOrDefault(c.Code)))
            .ToList();
    }
}
