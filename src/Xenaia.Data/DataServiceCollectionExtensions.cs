using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xenaia.Core.Options;
using Xenaia.Core.Outbox;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Provider-agnostic data-layer registrations. Providers (e.g.
    /// AddXenaiaPostgreSql) call this and then register the DbContext,
    /// attaching the interceptors registered here.
    /// </summary>
    public static IServiceCollection AddXenaiaData(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<DataOptions>(configuration);
        services.AddValidatedOptions<OutboxOptions>(configuration);
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.AddScoped<IBookingStore, EfBookingStore>();
        services.AddScoped<ICatalogStore, EfCatalogStore>();
        services.AddScoped<IAvailabilityStore, EfAvailabilityStore>();
        services.AddScoped<IOutboundBookingRequestStore, EfOutboundBookingRequestStore>();
        services.AddScoped<ISyncCheckpointStore, EfSyncCheckpointStore>();
        services.AddHostedService<OutboxDrainerService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DomainEventsToOutboxInterceptor>();
        services.AddSingleton<AuditStampInterceptor>();
        return services;
    }
}
