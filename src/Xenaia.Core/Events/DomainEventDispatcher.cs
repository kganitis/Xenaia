using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Xenaia.Core.Domain;

namespace Xenaia.Core.Events;

/// <summary>
/// Resolves all IDomainEventHandler&lt;TEvent&gt; registrations for the
/// event's runtime type and invokes them sequentially. Deliberately
/// dependency-free (no mediator library). Reflection is cached per event
/// type; synchronous handler exceptions are unwrapped so callers (the
/// outbox drainer) record the real exception, not TargetInvocationException.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, MethodInfo HandleMethod)> Cache = new();

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        var (handlerType, handleMethod) = Cache.GetOrAdd(domainEvent.GetType(), static eventType =>
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            return (handlerType, handleMethod);
        });

        foreach (var handler in services.GetServices(handlerType))
        {
            if (handler is null) continue;
            try
            {
                await (Task)handleMethod.Invoke(handler, [domainEvent, ct])!;
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            }
        }
    }
}
