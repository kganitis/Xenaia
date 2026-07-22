using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>
/// Hosted loop: one BookingInboundSweep per interval, a fresh scope per
/// iteration (hosted services are singletons; the sweep and its store/
/// provider dependencies are not). Mirrors TriagePollingService.
/// </summary>
public sealed class BookingPollingService(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    ILogger<BookingPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var interval = TimeSpan.FromSeconds(options.Value.Bookings.PollSeconds);
        logger.LogInformation("Bookings inbound polling started with interval {Interval}", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BookingInboundSweep>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Bookings inbound sweep failed; retrying next interval");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
