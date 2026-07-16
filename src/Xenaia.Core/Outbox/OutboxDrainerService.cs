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
            await store.MarkFailedAsync(
                message.Id,
                $"Unresolvable outbox message: type '{message.Type}' could not be materialized",
                ct);
            logger.LogWarning(
                "Parked unresolvable outbox message {MessageId} of type {MessageType}",
                message.Id, message.Type);
            return;
        }

        try
        {
            await dispatcher.DispatchAsync(domainEvent, ct);
            await store.MarkProcessedAsync(message.Id, timeProvider.GetUtcNow(), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await store.MarkFailedAsync(message.Id, ex.Message, ct);
            logger.LogWarning(ex, "Parked outbox message {MessageId} after handler failure", message.Id);
        }
    }
}
