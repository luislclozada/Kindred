using Kindred.MatchingService.Domain;
using Kindred.SharedKernel.Messaging;
using Kindred.SharedKernel.Messaging.Events;

namespace Kindred.MatchingService.Application;

// How matching-service learns about enrollments: it listens. One handler per integration event.

public sealed class StudentEnrolledInCourseHandler(ICourseRosterRepository rosters)
    : IIntegrationEventHandler<StudentEnrolledInCourseIntegrationEvent>
{
    public async Task Handle(StudentEnrolledInCourseIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var roster = await rosters.GetAsync(integrationEvent.CourseCode, cancellationToken);
        if (roster is null)
        {
            roster = CourseRoster.For(integrationEvent.CourseCode);
            rosters.Add(roster);
        }

        roster.Add(integrationEvent.StudentId, integrationEvent.StudentName);
        await rosters.SaveChangesAsync(cancellationToken);
    }
}

public sealed class StudentDroppedCourseHandler(ICourseRosterRepository rosters)
    : IIntegrationEventHandler<StudentDroppedCourseIntegrationEvent>
{
    public async Task Handle(StudentDroppedCourseIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var roster = await rosters.GetAsync(integrationEvent.CourseCode, cancellationToken);
        if (roster is null) return;

        roster.Remove(integrationEvent.StudentId);
        await rosters.SaveChangesAsync(cancellationToken);
    }
}
