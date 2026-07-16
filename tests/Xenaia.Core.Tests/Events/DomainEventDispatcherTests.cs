using Microsoft.Extensions.DependencyInjection;
using Xenaia.Core.Domain;
using Xenaia.Core.Events;

namespace Xenaia.Core.Tests.Events;

public class DomainEventDispatcherTests
{
    private sealed record PingEvent(DateTimeOffset OccurredAt) : IDomainEvent;
    private sealed record OtherEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class FirstPingHandler(Recorder recorder) : IDomainEventHandler<PingEvent>
    {
        public Task HandleAsync(PingEvent domainEvent, CancellationToken ct = default)
        {
            recorder.Calls.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondPingHandler(Recorder recorder) : IDomainEventHandler<PingEvent>
    {
        public Task HandleAsync(PingEvent domainEvent, CancellationToken ct = default)
        {
            recorder.Calls.Add("second");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Dispatches_to_every_registered_handler_of_the_event_type()
    {
        var recorder = new Recorder();
        var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddSingleton<IDomainEventHandler<PingEvent>, FirstPingHandler>()
            .AddSingleton<IDomainEventHandler<PingEvent>, SecondPingHandler>()
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider);

        await dispatcher.DispatchAsync(new PingEvent(DateTimeOffset.UnixEpoch));

        Assert.Equal(["first", "second"], recorder.Calls);
    }

    [Fact]
    public async Task Event_with_no_handlers_is_a_no_op()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider);

        var exception = await Record.ExceptionAsync(
            () => dispatcher.DispatchAsync(new OtherEvent(DateTimeOffset.UnixEpoch)));

        Assert.Null(exception);
    }
}
