namespace Kindred.SharedKernel.Messaging.Events;

/// <summary>Published by student-service when a student enrolls in a course.</summary>
public sealed record StudentEnrolledInCourseIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid StudentId,
    string StudentName,
    string CourseCode) : IIntegrationEvent;

/// <summary>Published by student-service when a student drops a course.</summary>
public sealed record StudentDroppedCourseIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid StudentId,
    string CourseCode) : IIntegrationEvent;
