using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Xenaia.Data.PostgreSql;

/// <summary>
/// Applies pending migrations at host startup when Data:AutoMigrate is
/// true. Throws on failure: a misconfigured or unreachable database aborts
/// startup rather than half-running (fail closed).
/// </summary>
public sealed class MigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DataOptions> options,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoMigrate) return;

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<XenaiaDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
