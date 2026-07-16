using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.Domain;
using Xenaia.Core.Events;
using Xenaia.Core.Outbox;

namespace Xenaia.Core.Tests.Outbox;

public class OutboxDrainerServiceTests
{
    private sealed record DrainedEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class RecordingHandler : IDomainEventHandler<DrainedEvent>
    {
        public int Handled { get; private set; }

        public Task HandleAsync(DrainedEvent domainEvent, CancellationToken ct = default)
        {
            Handled++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<DrainedEvent>
    {
        public Task HandleAsync(DrainedEvent domainEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("handler exploded");
    }

    /// <summary>In-memory IOutboxStore. Unprocessed = ProcessedAt and Error both null.</summary>
    private sealed class FakeOutboxStore : IOutboxStore
    {
        private readonly List<OutboxMessage> _messages = [];
        public int FailGetCalls { get; set; }
        public int GetCalls { get; private set; }

        public IReadOnlyList<OutboxMessage> Messages
        {
            get { lock (_messages) return [.. _messages]; }
        }

        public Task AppendAsync(IEnumerable<OutboxMessage> messages, CancellationToken ct = default)
        {
            lock (_messages) _messages.AddRange(messages);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default)
        {
            GetCalls++;
            if (FailGetCalls > 0)
            {
                FailGetCalls--;
                throw new InvalidOperationException("store unavailable");
            }
            lock (_messages)
            {
                IReadOnlyList<OutboxMessage> batch = _messages
                    .Where(m => m.ProcessedAt == null && m.Error == null)
                    .OrderBy(m => m.OccurredAt)
                    .Take(batchSize)
                    .ToList();
                return Task.FromResult(batch);
            }
        }

        public Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken ct = default)
            => Mutate(id, m => m with { ProcessedAt = processedAt });

        public Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default)
            => Mutate(id, m => m with { Error = error });

        private Task Mutate(Guid id, Func<OutboxMessage, OutboxMessage> change)
        {
            lock (_messages)
            {
                var index = _messages.FindIndex(m => m.Id == id);
                _messages[index] = change(_messages[index]);
            }
            return Task.CompletedTask;
        }
    }

    private static (OutboxDrainerService Drainer, FakeOutboxStore Store, FakeTimeProvider Time, RecordingHandler Handler)
        BuildDrainer(IDomainEventHandler<DrainedEvent>? handlerOverride = null)
    {
        var store = new FakeOutboxStore();
        var handler = new RecordingHandler();
        var services = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>()
            .AddSingleton(handlerOverride ?? (IDomainEventHandler<DrainedEvent>)handler)
            .BuildServiceProvider();
        var time = new FakeTimeProvider();
        var drainer = new OutboxDrainerService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()),
            time,
            NullLogger<OutboxDrainerService>.Instance);
        return (drainer, store, time, handler);
    }

    private static async Task WaitForAsync(Func<bool> condition, FakeTimeProvider? advance = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met within 5s");
            advance?.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Dispatches_pending_message_and_marks_processed()
    {
        var (drainer, store, _, handler) = BuildDrainer();
        await store.AppendAsync([OutboxMessage.From(new DrainedEvent(DateTimeOffset.UtcNow))]);

        await drainer.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Messages.All(m => m.ProcessedAt != null));
        await drainer.StopAsync(CancellationToken.None);

        Assert.Equal(1, handler.Handled);
        Assert.Null(store.Messages.Single().Error);
    }

    [Fact]
    public async Task Parks_unresolvable_message_with_descriptive_error()
    {
        var (drainer, store, _, _) = BuildDrainer();
        await store.AppendAsync([new OutboxMessage
        {
            Type = "No.Such.Type, No.Such.Assembly",
            Payload = "{}",
            OccurredAt = DateTimeOffset.UtcNow,
        }]);

        await drainer.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Messages.Single().Error != null);
        await drainer.StopAsync(CancellationToken.None);

        var parked = store.Messages.Single();
        Assert.Null(parked.ProcessedAt);
        Assert.Contains("No.Such.Type", parked.Error);
    }

    [Fact]
    public async Task Parks_message_when_handler_throws_and_records_real_error()
    {
        var (drainer, store, _, _) = BuildDrainer(new ThrowingHandler());
        await store.AppendAsync([OutboxMessage.From(new DrainedEvent(DateTimeOffset.UtcNow))]);

        await drainer.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Messages.Single().Error != null);
        await drainer.StopAsync(CancellationToken.None);

        Assert.Contains("handler exploded", store.Messages.Single().Error);
    }

    [Fact]
    public async Task Continues_batch_after_poison_message()
    {
        var (drainer, store, _, handler) = BuildDrainer();
        var poison = new OutboxMessage
        {
            Type = "No.Such.Type, No.Such.Assembly",
            Payload = "{}",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        await store.AppendAsync([poison, OutboxMessage.From(new DrainedEvent(DateTimeOffset.UtcNow))]);

        await drainer.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Messages.Count(m => m.ProcessedAt != null || m.Error != null) == 2);
        await drainer.StopAsync(CancellationToken.None);

        Assert.Equal(1, handler.Handled);
        Assert.Equal(1, store.Messages.Count(m => m.Error != null));
        Assert.Equal(1, store.Messages.Count(m => m.ProcessedAt != null));
    }

    [Fact]
    public async Task Survives_store_failure_and_drains_on_a_later_tick()
    {
        var (drainer, store, time, handler) = BuildDrainer();
        store.FailGetCalls = 1;
        await store.AppendAsync([OutboxMessage.From(new DrainedEvent(DateTimeOffset.UtcNow))]);

        await drainer.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.GetCalls >= 1);
        await WaitForAsync(() => store.Messages.All(m => m.ProcessedAt != null), advance: time);
        await drainer.StopAsync(CancellationToken.None);

        Assert.Equal(1, handler.Handled);
    }

    [Fact]
    public async Task Stops_promptly_on_shutdown()
    {
        var (drainer, _, _, _) = BuildDrainer();

        await drainer.StartAsync(CancellationToken.None);
        var stop = drainer.StopAsync(CancellationToken.None);
        var finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(stop, finished);
    }
}
