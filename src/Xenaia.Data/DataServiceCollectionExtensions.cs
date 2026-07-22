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
        // Providers register their own interpreter (e.g. the PostgreSQL one)
        // before calling this, so TryAdd leaves it in place; otherwise the
        // no-op default classifies nothing and save failures propagate raw.
        services.TryAddSingleton<IDbExceptionInterpreter, NullDbExceptionInterpreter>();
        services.AddSingleton<DomainEventsToOutboxInterceptor>();
        services.AddSingleton<AuditStampInterceptor>();
        return services;
    }
}
