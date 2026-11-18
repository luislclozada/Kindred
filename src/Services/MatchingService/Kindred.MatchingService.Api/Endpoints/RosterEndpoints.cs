using Kindred.MatchingService.Application;
using Kindred.SharedKernel.Mediator;

namespace Kindred.MatchingService.Api.Endpoints;

public static class RosterEndpoints
{
    public static void MapRosterEndpoints(this IEndpointRouteBuilder matching) =>
        matching.MapGet("/courses/{courseCode}/roster", async (string courseCode, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetCourseRosterQuery(courseCode), ct)));
}
