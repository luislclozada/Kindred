using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Application.Queries;

namespace Kindred.StudentService.Api.Endpoints;

public static class CourseEndpoints
{
    public static void MapCourseEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGet("/courses", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListCoursesQuery(), ct)));
}
