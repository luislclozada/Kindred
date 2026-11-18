using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Queries;

public sealed record ListStudentsQuery : IQuery<IReadOnlyList<StudentSummaryDto>>;

public sealed class ListStudentsHandler(IStudentRepository students)
    : IQueryHandler<ListStudentsQuery, IReadOnlyList<StudentSummaryDto>>
{
    public async Task<IReadOnlyList<StudentSummaryDto>> Handle(ListStudentsQuery query, CancellationToken cancellationToken) =>
        (await students.ListAsync(cancellationToken))
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.ToSummaryDto())
            .ToList();
}

public sealed record GetStudentQuery(Guid StudentId) : IQuery<Result<StudentDto>>;

public sealed class GetStudentHandler(IStudentRepository students)
    : IQueryHandler<GetStudentQuery, Result<StudentDto>>
{
    public async Task<Result<StudentDto>> Handle(GetStudentQuery query, CancellationToken cancellationToken)
    {
        var student = await students.GetByIdAsync(query.StudentId, cancellationToken);
        return student is null ? StudentErrors.StudentNotFound(query.StudentId) : student.ToDto();
    }
}
