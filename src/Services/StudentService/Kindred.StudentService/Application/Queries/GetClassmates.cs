using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Queries;

/// <summary>Other students who share at least one course with this one, most shared courses first.</summary>
public sealed record GetClassmatesQuery(Guid StudentId) : IQuery<Result<IReadOnlyList<ClassmateDto>>>;

public sealed class GetClassmatesHandler(IStudentRepository students)
    : IQueryHandler<GetClassmatesQuery, Result<IReadOnlyList<ClassmateDto>>>
{
    public async Task<Result<IReadOnlyList<ClassmateDto>>> Handle(GetClassmatesQuery query, CancellationToken cancellationToken)
    {
        var student = await students.GetByIdAsync(query.StudentId, cancellationToken);
        if (student is null) return StudentErrors.StudentNotFound(query.StudentId);

        var myCourses = student.Enrollments.Select(e => e.CourseCode).ToHashSet();
        if (myCourses.Count == 0) return Result.Success<IReadOnlyList<ClassmateDto>>([]);

        var others = await students.ListSharingAnyCourseAsync(student.Id, myCourses, cancellationToken);

        return others
            .Select(other =>
            {
                var shared = other.Enrollments
                    .Select(e => e.CourseCode)
                    .Where(myCourses.Contains)
                    .Select(c => c.Value)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                return new ClassmateDto(other.Id, other.DisplayName, other.Program, shared, shared.Count);
            })
            .OrderByDescending(c => c.SharedCount)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
