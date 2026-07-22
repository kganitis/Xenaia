using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>
/// Hosted consumer for bookings outbound (spec 6.4 step 2). Runs startup
/// recovery once, then drains the wake-up channel: a fresh scope per drain
/// cycle owns one <see cref="BookingPusher"/> and processes every request id
/// currently queued. All logic lives in the pusher; this service is only the
/// loop and scope plumbing. Registration is unconditional (like the other Sync
/// hosted services); host-level flow gating (Sync:Flows:BookingsOutbound)
/// arrives in Task 16.
/// </summary>
public sealed class BookingPushService(
    IServiceScopeFactory scopeFactory,
    BookingChannel channel,
    ILogger<BookingPushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BookingPusher>().RecoverAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Booking startup recovery failed; continuing to drain new work");
        }

        var reader = channel.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool hasWork;
            try
            {
                hasWork = await reader.WaitToReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (!hasWork)
                break;

            using var scope = scopeFactory.CreateScope();
            var pusher = scope.ServiceProvider.GetRequiredService<BookingPusher>();

            while (reader.TryRead(out var requestId))
            {
                try
                {
                    await pusher.ProcessAsync(requestId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Booking request {Id} failed unexpectedly", requestId);
                }
            }
        }
    }
}
