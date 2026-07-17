using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Xenaia.Modules.Triage.Processing;

/// <summary>
/// Hosted loop: one TriageSweep per interval, a fresh scope per iteration
/// (hosted services are singletons; the sweep and the provider are not).
/// </summary>
public sealed class TriagePollingService(
    IServiceScopeFactory scopeFactory,
    IOptions<TriageOptions> options,
    ILogger<TriagePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        logger.LogInformation("Triage polling started with interval {Interval}", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TriageSweep>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Triage sweep failed; retrying next interval");
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
