namespace Kindred.SharedKernel.Messaging.Events;

/// <summary>
/// Published by student-service when a student's display name changes. Services that keep a copy
/// of the name (matching-service's course rosters) replace it. Not published when only the
/// program changes.
/// </summary>
public sealed record StudentRenamedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid StudentId,
    string DisplayName) : IIntegrationEvent;
