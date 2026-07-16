namespace Xenaia.Core.Domain;

/// <summary>
/// Consistency boundary. State changes happen through methods that may
/// Raise domain events; persistence dequeues them into the outbox.
/// </summary>
public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id)
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
