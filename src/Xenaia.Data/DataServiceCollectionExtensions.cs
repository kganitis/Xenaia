using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xenaia.Core.Options;
using Xenaia.Core.Outbox;

namespace Xenaia.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Provider-agnostic data-layer registrations. Providers (e.g.
    /// AddXenaiaPostgreSql) call this and then register the DbContext.
    /// </summary>
    public static IServiceCollection AddXenaiaData(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<DataOptions>(configuration);
        services.AddValidatedOptions<OutboxOptions>(configuration);
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.AddHostedService<OutboxDrainerService>();
        return services;
    }
}
