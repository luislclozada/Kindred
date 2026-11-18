namespace Kindred.SharedKernel.Messaging;

/// <summary>
/// An event that crosses a service boundary. Services talk over RabbitMQ, never by calling each
/// other over HTTP. Contracts live in Kindred.SharedKernel/Messaging/Events so both sides compile
/// against the same shape.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IEventBus
{
    /// <summary>Publishes using the event's runtime type, so the payload is never truncated to the interface.</summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}
