using Kindred.SharedKernel.Api;
using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Application.Commands;
using Kindred.StudentService.Application.Queries;

namespace Kindred.StudentService.Api.Endpoints;

public sealed record RegisterStudentRequest(string? DisplayName, string? Email, string? Program);

public sealed record EnrollRequest(string? CourseCode);

public static class StudentEndpoints
{
    public static void MapStudentEndpoints(this IEndpointRouteBuilder api)
    {
        var students = api.MapGroup("/students");

        students.MapPost("/", async (RegisterStudentRequest request, IMediator mediator, CancellationToken ct) =>
            (await mediator.Send(new RegisterStudentCommand(request.DisplayName, request.Email, request.Program), ct))
            .ToCreatedResult(student => $"/api/v1/students/{student.Id}"));

        students.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListStudentsQuery(), ct)));

        students.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            (await mediator.Send(new GetStudentQuery(id), ct)).ToHttpResult());

        students.MapPost("/{id:guid}/enrollments", async (Guid id, EnrollRequest request, IMediator mediator, CancellationToken ct) =>
            (await mediator.Send(new EnrollInCourseCommand(id, request.CourseCode), ct)).ToHttpResult());

        students.MapDelete("/{id:guid}/enrollments/{courseCode}", async (Guid id, string courseCode, IMediator mediator, CancellationToken ct) =>
            (await mediator.Send(new DropCourseCommand(id, courseCode), ct)).ToHttpResult());

        students.MapGet("/{id:guid}/classmates", async (Guid id, IMediator mediator, CancellationToken ct) =>
            (await mediator.Send(new GetClassmatesQuery(id), ct)).ToHttpResult());
    }
}
