using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Catalog;

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

        // Availability inbound (sheet-driven fetch, spec 6.2): the parser is
        // stateless so one instance serves every call; the fetch service is
        // scoped, resolved once per Task 16 endpoint call. ISpreadsheetGateway
        // is a required dependency here (unlike the outbound pusher's optional
        // one): the endpoint that will call this service already gates on a
        // spreadsheet provider being registered before resolving it.
        services.AddSingleton<SheetCombinationParser>();
        services.AddScoped<AvailabilityFetchService>();

        // Catalog sync (spec 6.5): the participant-type cache is a singleton
        // read-through over the scoped ICatalogStore (it opens its own scope
        // per miss); the sync service is scoped, resolved fresh per refresh
        // tick or Task 16 endpoint call; the refresh service is the hosted
        // warm-start-then-daily trigger.
        services.AddSingleton<ParticipantTypeCache>();
        services.AddScoped<CatalogSyncService>();
        services.AddHostedService<CatalogRefreshService>();

        return services;
    }
}
