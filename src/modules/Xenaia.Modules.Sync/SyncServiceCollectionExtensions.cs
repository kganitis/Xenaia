using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Modules.Sync.Availability;

namespace Xenaia.Modules.Sync;

public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sync module: options + validator, plus the availability
    /// patch flow's channel (singleton wake-up queue) and service (scoped,
    /// owns a store per call). Later tasks append the remaining flow and
    /// hosted services to this one method as BrightTide and Google Workspace
    /// adapters come online.
    /// </summary>
    public static IServiceCollection AddSyncModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<SyncOptions>(configuration);
        services.AddSingleton<IValidateOptions<SyncOptions>, SyncOptionsValidator>();

        services.AddSingleton(sp =>
            new AvailabilityChannel(sp.GetRequiredService<IOptions<SyncOptions>>().Value.Availability.ChannelCapacity));
        services.AddScoped<AvailabilityPatchService>();

        // Availability outbound consumer: the pusher and its per-drain sheet
        // buffer are scoped (a fresh pair per drain cycle), the processor is the
        // hosted drain loop. ISpreadsheetGateway is an optional constructor
        // dependency of the pusher; when no spreadsheet provider is registered
        // it stays null and no sheet write-back is attempted.
        services.AddScoped<SheetWriteBuffer>();
        services.AddScoped<AvailabilityPusher>();
        services.AddHostedService<AvailabilityProcessorService>();

        return services;
    }
}
