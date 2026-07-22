using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Bookings;
using Xenaia.Modules.Sync.Catalog;

namespace Xenaia.Modules.Sync;

public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sync module: options + validator, the five flows' channels
    /// and application services (all scoped/singleton, resolved by the Api
    /// endpoints), and a hosted background service per flow. Each flow's hosted
    /// service is gated by its <c>Sync:Flows:*</c> boolean (default true); the
    /// gating decision is read from configuration here, at registration time.
    /// <paramref name="spreadsheetConfigured"/> is passed by the host when a
    /// spreadsheet provider is registered: only then does the validator require
    /// the sheet names (spec section 7).
    /// </summary>
    public static IServiceCollection AddSyncModule(
        this IServiceCollection services, IConfiguration configuration,
        bool spreadsheetConfigured = false)
    {
        services.AddValidatedOptions<SyncOptions>(configuration);
        services.AddSingleton<IValidateOptions<SyncOptions>, SyncOptionsValidator>();
        if (spreadsheetConfigured)
            services.PostConfigure<SyncOptions>(o => o.RequireSheetNames = true);

        // Flow gating (spec section 6): the DB rows are the durable queue, so the
        // application services always register (the endpoints need them); only
        // the hosted drain/poll loops are gated. Bound manually here because the
        // validated options are not built yet at registration time.
        var flows = configuration.GetSection(SyncOptions.SectionName).Get<SyncOptions>()?.Flows
            ?? new FlowsOptions();

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
        if (flows.AvailabilityOutbound)
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
        if (flows.Catalog)
            services.AddHostedService<CatalogRefreshService>();

        // Bookings inbound (spec 6.3): the sweep is scoped, resolved fresh per
        // BookingPollingService tick; the polling service is the hosted
        // checkpoint-driven loop, gated by the BookingsInbound flow flag.
        services.AddScoped<BookingInboundSweep>();
        if (flows.BookingsInbound)
            services.AddHostedService<BookingPollingService>();

        // Bookings outbound (spec 6.4): the channel is a singleton wake-up
        // queue sized by Bookings.ChannelCapacity; the enqueuer (the Api
        // endpoints call it) and the pusher are scoped, each resolved fresh
        // per call or drain cycle; the push service is the hosted
        // recovery-then-drain loop, gated by the BookingsOutbound flow flag.
        services.AddSingleton(sp =>
            new BookingChannel(sp.GetRequiredService<IOptions<SyncOptions>>().Value.Bookings.ChannelCapacity));
        services.AddScoped<OutboundBookingEnqueuer>();
        services.AddScoped<BookingPusher>();
        if (flows.BookingsOutbound)
            services.AddHostedService<BookingPushService>();

        return services;
    }
}
