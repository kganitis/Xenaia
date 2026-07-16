using Microsoft.Extensions.DependencyInjection;
using Xenaia.Core.Domain;

namespace Xenaia.Core.Events;

/// <summary>
/// Resolves all IDomainEventHandler&lt;TEvent&gt; registrations for the
/// event's runtime type and invokes them sequentially. Deliberately
/// dependency-free (no mediator library): ~30 lines we fully own.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

        foreach (var handler in services.GetServices(handlerType))
        {
            if (handler is null) continue;
            await (Task)handleMethod.Invoke(handler, [domainEvent, ct])!;
        }
    }
}
