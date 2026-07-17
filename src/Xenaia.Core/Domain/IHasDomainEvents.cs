namespace Xenaia.Core.Domain;

/// <summary>
/// Non-generic view of an aggregate root's event queue, for infrastructure
/// that must drain events without knowing the id type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}
