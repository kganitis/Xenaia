using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Xenaia.Data.Tests;

/// <summary>
/// One Postgres container for the whole test collection; the real
/// InitialCreate migration (never EnsureCreated) is applied once at start.
/// Requires Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public XenaiaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<XenaiaDbContext>()
            .UseNpgsql(ConnectionString, o => o.MigrationsAssembly("Xenaia.Data.PostgreSql"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new DomainEventsToOutboxInterceptor(),
                new AuditStampInterceptor(TimeProvider.System))
            .Options);
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
