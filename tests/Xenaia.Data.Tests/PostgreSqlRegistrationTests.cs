using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
