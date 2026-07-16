using Microsoft.Extensions.DependencyInjection;
using Xenaia.Core.Domain;
using Xenaia.Core.Events;

namespace Xenaia.Core.Tests.Events;

public class DomainEventDispatcherHardeningTests
{
    private sealed record HardeningEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class SyncThrowingHandler : IDomainEventHandler<HardeningEvent>
    {
        public Task HandleAsync(HardeningEvent domainEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class CountingHandler : IDomainEventHandler<HardeningEvent>
    {
        public int Calls { get; private set; }

        public Task HandleAsync(HardeningEvent domainEvent, CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_exception_surfaces_as_its_original_type()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<HardeningEvent>, SyncThrowingHandler>()
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new HardeningEvent(DateTimeOffset.UtcNow)));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Repeated_dispatch_of_same_event_type_still_routes()
    {
        var handler = new CountingHandler();
        var provider = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<HardeningEvent>>(handler)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider);
        var domainEvent = new HardeningEvent(DateTimeOffset.UtcNow);

        await dispatcher.DispatchAsync(domainEvent);
        await dispatcher.DispatchAsync(domainEvent);

        Assert.Equal(2, handler.Calls);
    }
}
