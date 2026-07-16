using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using Xenaia.Data.PostgreSql;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class PostgreSqlRegistrationTests(PostgresFixture fixture)
{
    [Fact]
    public void Registers_context_migrator_and_drainer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:ConnectionString"] = fixture.ConnectionString,
                ["Outbox:BatchSize"] = "50",
            })
            .Build();

        var services = new ServiceCollection().AddXenaiaPostgreSql(configuration);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<XenaiaDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Xenaia.Core.Outbox.IOutboxStore>());
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(MigrationHostedService));
    }

    [Fact]
    public void Migrator_registers_before_the_drainer_so_it_starts_first()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:ConnectionString"] = fixture.ConnectionString,
                ["Outbox:BatchSize"] = "50",
            })
            .Build();

        var services = new ServiceCollection().AddXenaiaPostgreSql(configuration);

        var hostedImplementations = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();
        var migratorIndex = hostedImplementations.IndexOf(typeof(MigrationHostedService));
        var drainerIndex = hostedImplementations.IndexOf(typeof(Xenaia.Core.Outbox.OutboxDrainerService));
        Assert.True(migratorIndex >= 0 && drainerIndex >= 0);
        Assert.True(migratorIndex < drainerIndex,
            "MigrationHostedService must register before OutboxDrainerService");
    }

    [Fact]
    public async Task Context_from_registration_reaches_the_database()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:ConnectionString"] = fixture.ConnectionString,
                ["Outbox:BatchSize"] = "50",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddXenaiaPostgreSql(configuration)
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<XenaiaDbContext>();
        Assert.True(await context.Database.CanConnectAsync());
    }
}
