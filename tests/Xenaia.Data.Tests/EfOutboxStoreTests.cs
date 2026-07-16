using Microsoft.EntityFrameworkCore;
using Xenaia.Core.Outbox;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class EfOutboxStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private XenaiaDbContext _context = null!;

    private IOutboxStore CreateStore() => new EfOutboxStore(_context);

    public async Task InitializeAsync()
    {
        _context = fixture.CreateContext();
        await _context.Outbox.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private static OutboxMessage Message(
        DateTimeOffset occurredAt,
        DateTimeOffset? processedAt = null,
        string? error = null) => new()
    {
        Type = "Meridian.Trails.TestEvent",
        Payload = """{"kind":"test"}""",
        OccurredAt = occurredAt,
        ProcessedAt = processedAt,
        Error = error,
    };

    [Fact]
    public async Task Append_then_get_round_trips()
    {
        var store = CreateStore();
        var message = Message(DateTimeOffset.UtcNow);

        await store.AppendAsync([message]);
        var unprocessed = await store.GetUnprocessedAsync(10);

        var loaded = Assert.Single(unprocessed);
        Assert.Equal(message.Id, loaded.Id);
        Assert.Equal(message.Type, loaded.Type);
        Assert.Equal(message.Payload, loaded.Payload);
    }

    [Fact]
    public async Task Unprocessed_excludes_processed_and_parked_messages()
    {
        var store = CreateStore();
        var pending = Message(DateTimeOffset.UtcNow);
        var processed = Message(DateTimeOffset.UtcNow, processedAt: DateTimeOffset.UtcNow);
        var parked = Message(DateTimeOffset.UtcNow, error: "poison");

        await store.AppendAsync([pending, processed, parked]);
        var unprocessed = await store.GetUnprocessedAsync(10);

        Assert.Equal(pending.Id, Assert.Single(unprocessed).Id);
    }

    [Fact]
    public async Task Unprocessed_is_oldest_first_and_respects_batch_size()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var newest = Message(now);
        var oldest = Message(now.AddMinutes(-10));
        var middle = Message(now.AddMinutes(-5));

        await store.AppendAsync([newest, oldest, middle]);
        var batch = await store.GetUnprocessedAsync(2);

        Assert.Equal(2, batch.Count);
        Assert.Equal(oldest.Id, batch[0].Id);
        Assert.Equal(middle.Id, batch[1].Id);
    }

    [Fact]
    public async Task Mark_processed_persists_and_excludes_from_future_batches()
    {
        var store = CreateStore();
        var message = Message(DateTimeOffset.UtcNow);
        var processedAt = DateTimeOffset.UtcNow;

        await store.AppendAsync([message]);
        await store.MarkProcessedAsync(message.Id, processedAt);

        Assert.Empty(await store.GetUnprocessedAsync(10));
        var stored = await _context.Outbox.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.Error);
    }

    [Fact]
    public async Task Mark_failed_parks_with_the_error_text()
    {
        var store = CreateStore();
        var message = Message(DateTimeOffset.UtcNow);

        await store.AppendAsync([message]);
        await store.MarkFailedAsync(message.Id, "handler exploded");

        Assert.Empty(await store.GetUnprocessedAsync(10));
        var stored = await _context.Outbox.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal("handler exploded", stored.Error);
        Assert.Null(stored.ProcessedAt);
    }
}
