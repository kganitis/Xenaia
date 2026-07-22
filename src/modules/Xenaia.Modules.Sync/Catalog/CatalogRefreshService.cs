using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Modules.Sync.Catalog;

/// <summary>
/// Hosted trigger for catalog sync (spec 6.5): a warm-start refresh on boot
/// when the catalog is empty, then a daily refresh at
/// <c>Sync:Catalog:RefreshUtcTime</c>. On error, logs and retries in one
/// hour rather than waiting for the next scheduled day. All sync logic lives
/// in <see cref="CatalogSyncService"/>; this service is only the loop, the
/// empty-catalog check, and scope plumbing.
/// </summary>
public sealed class CatalogRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    TimeProvider clock,
    ILogger<CatalogRefreshService> logger,
    Func<TimeSpan, CancellationToken, Task>? delayer = null) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayer =
        delayer ?? ((delay, token) => Task.Delay(delay, clock, token));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (await IsCatalogEmptyAsync(stoppingToken))
            {
                logger.LogInformation("Catalog is empty; running a warm-start refresh");
                await RunRefreshAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog warm-start refresh failed; continuing to the daily schedule");
        }

        var refreshTime = TimeOnly.Parse(options.Value.Catalog.RefreshUtcTime, CultureInfo.InvariantCulture);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _delayer(TimeUntilNext(refreshTime, clock.GetUtcNow()), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Retry loop: on failure, wait exactly RetryDelay and try again
            // (not the top-of-loop schedule wait, which would silently
            // stretch the "retry in 1 hour" promise out to the next day
            // since today's scheduled instant has already passed). Falls
            // through to the daily schedule wait only once a run succeeds.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunRefreshAsync(stoppingToken);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Catalog refresh failed; retrying in 1 hour");
                    try
                    {
                        await _delayer(RetryDelay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task<bool> IsCatalogEmptyAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var products = await store.GetProductsAsync(ct);
        return products.Count == 0;
    }

    private async Task RunRefreshAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CatalogSyncService>();
        await service.RefreshAsync(ct);
    }

    /// <summary>The next UTC instant refreshTime occurs at, today if it has
    /// not yet passed, tomorrow otherwise.</summary>
    private static TimeSpan TimeUntilNext(TimeOnly refreshTime, DateTimeOffset now)
    {
        var todayAt = new DateTimeOffset(now.Date, TimeSpan.Zero) + refreshTime.ToTimeSpan();
        var next = todayAt > now ? todayAt : todayAt.AddDays(1);
        return next - now;
    }
}
