using Kindred.SharedKernel.Domain;

namespace Kindred.StudentService.Domain;

// Domain events: facts about the Student aggregate, raised inside it. Command handlers translate
// them into integration events (Kindred.SharedKernel.Messaging.Events) after the save succeeds.

public sealed record StudentEnrolledInCourse(
    Guid StudentId, string StudentName, CourseCode CourseCode, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record StudentRenamed(
    Guid StudentId, string DisplayName, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record StudentDroppedCourse(
    Guid StudentId, CourseCode CourseCode, DateTimeOffset OccurredAt) : IDomainEvent;
