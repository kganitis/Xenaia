using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>
/// Hosted consumer for availability outbound (spec 6.1 step 3). Runs startup
/// recovery once, then drains the wake-up channel: a fresh scope per drain
/// cycle owns one <see cref="AvailabilityPusher"/> and one shared
/// <see cref="SheetWriteBuffer"/>, reads every item currently queued, and
/// flushes the buffer once the channel is momentarily empty. All logic lives
/// in the pusher and buffer; this service is only the loop and scope plumbing.
/// </summary>
public sealed class AvailabilityProcessorService(
    IServiceScopeFactory scopeFactory,
    AvailabilityChannel channel,
    IOptions<SyncOptions> options,
    ILogger<AvailabilityProcessorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AvailabilityPusher>().RecoverAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Availability startup recovery failed; continuing to drain new work");
        }

        var reader = channel.Reader;
        var getSheetName = options.Value.Availability.GetSheetName;

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
            var services = scope.ServiceProvider;
            var pusher = services.GetRequiredService<AvailabilityPusher>();
            var buffer = services.GetRequiredService<SheetWriteBuffer>();
            var gateway = services.GetService<ISpreadsheetGateway>();

            while (reader.TryRead(out var item))
            {
                try
                {
                    await pusher.ProcessAsync(item, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Availability item {Id} failed unexpectedly", item.AvailabilityId);
                }
            }

            if (gateway is not null && !buffer.IsEmpty)
            {
                try
                {
                    await buffer.FlushAsync(gateway, getSheetName, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Availability sheet write-back flush failed for this drain cycle");
                }
            }
        }
    }
}
