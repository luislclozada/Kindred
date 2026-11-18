namespace Kindred.SharedKernel.Domain;

/// <summary>An object with identity. Two entities are equal when their ids are equal.</summary>
public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>A domain event: something that happened inside an aggregate, in the past tense.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Consistency boundary. All state changes go through the aggregate's methods; business rules
/// live here, not in handlers. Raised events are collected and drained by the application layer
/// after a successful commit.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
