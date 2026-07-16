using Xenaia.Core.Domain;

namespace Xenaia.Core.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default);
}
