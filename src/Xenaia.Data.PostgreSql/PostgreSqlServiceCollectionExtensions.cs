using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Data;

namespace Xenaia.Data.PostgreSql;

public static class PostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// PostgreSQL provider registration: provider-agnostic data services,
    /// the context (snake_case naming, migrations in this assembly), and
    /// the startup migration runner. The connection string flows through
    /// validated DataOptions, so a missing or empty value stops the host.
    /// </summary>
    public static IServiceCollection AddXenaiaPostgreSql(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Hosted services start in registration order: the migrator must register
        // before AddXenaiaData's drainer so the schema exists before the first drain.
        services.AddHostedService<MigrationHostedService>();
        // Registered before AddXenaiaData so its TryAdd default stays out: the
        // EF stores read this to map Npgsql 23505 to a domain exception.
        services.AddSingleton<IDbExceptionInterpreter, PostgresDbExceptionInterpreter>();
        services.AddXenaiaData(configuration);
        services.AddDbContext<XenaiaDbContext>((provider, options) => options
            .UseNpgsql(
                provider.GetRequiredService<IOptions<DataOptions>>().Value.ConnectionString,
                npgsql => npgsql.MigrationsAssembly("Xenaia.Data.PostgreSql"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                provider.GetRequiredService<DomainEventsToOutboxInterceptor>(),
                provider.GetRequiredService<AuditStampInterceptor>()));
        return services;
    }
}
