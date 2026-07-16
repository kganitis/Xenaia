using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Core.Events;

namespace Xenaia.Core.Outbox;

/// <summary>
/// Polls the outbox and dispatches pending domain events (oldest first).
/// Strictly single-instance: no claim locking; the single-tenant template
/// runs one host. Poison messages are parked (Error set) and never retried
/// automatically; delivery is at-least-once across restarts.
/// </summary>
public sealed class OutboxDrainerService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDrainerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            do
            {
                await DrainBatchAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown. An unmarked in-flight message re-runs next start.
        }
    }

    private async Task DrainBatchAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

            var batch = await store.GetUnprocessedAsync(options.Value.BatchSize, ct);
            foreach (var message in batch)
            {
                await ProcessAsync(store, dispatcher, message, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox drain batch failed; retrying next tick");
        }
    }

    private async Task ProcessAsync(
        IOutboxStore store,
        IDomainEventDispatcher dispatcher,
        OutboxMessage message,
        CancellationToken ct)
    {
        var domainEvent = message.ToDomainEvent();
        if (domainEvent is null)
        {
            await TryParkAsync(
                store, message,
                $"Unresolvable outbox message: type '{message.Type}' could not be materialized",
                cause: null, ct);
            return;
        }

        try
        {
            await dispatcher.DispatchAsync(domainEvent, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryParkAsync(store, message, ex.Message, ex, ct);
            return;
        }

        // Dispatch succeeded: a persistence failure here must not park a
        // delivered message. Let it propagate to the batch-level catch so
        // the message redelivers next tick (at-least-once).
        await store.MarkProcessedAsync(message.Id, timeProvider.GetUtcNow(), ct);
    }

    private async Task TryParkAsync(
        IOutboxStore store,
        OutboxMessage message,
        string error,
        Exception? cause,
        CancellationToken ct)
    {
        try
        {
            await store.MarkFailedAsync(message.Id, error, ct);
            logger.LogWarning(cause, "Parked outbox message {MessageId}: {Error}", message.Id, error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception parkException)
        {
            // Parking failed: leave the message unprocessed (it retries next
            // tick) and keep draining the rest of the batch.
            logger.LogError(parkException,
                "Failed to park outbox message {MessageId}; it will retry next tick", message.Id);
        }
    }
}
