using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Modules.Sync.Catalog;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;

namespace Xenaia.Modules.Sync.Tests.Catalog;

/// <summary>Only the warm-start behavior is cheap to unit-test without
/// driving the real daily schedule: the outer polling delay is given a
/// delayer that blocks until cancellation, so StartAsync runs exactly the
/// warm-start refresh and then idles until StopAsync cancels it.</summary>
public class CatalogRefreshServiceTests
{
    [Fact]
    public async Task Runs_a_warm_start_refresh_on_boot_when_the_catalog_is_empty()
    {
        var providerFake = new InMemoryBookingSystemProvider();
        providerFake.SeedProduct(new ProductSnapshot(42, "Sunset Kayak Tour", null));
        var catalogStore = new FakeCatalogStore();
        var services = new ServiceCollection()
            .AddSingleton<IBookingSystemProvider>(providerFake)
            .AddSingleton<ICatalogStore>(catalogStore)
            .AddSingleton(Options.Create(new SyncOptions()))
            .AddSingleton<ILogger<CatalogSyncService>>(NullLogger<CatalogSyncService>.Instance)
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ParticipantTypeCache>()
            .AddScoped<CatalogSyncService>()
            .BuildServiceProvider();

        var refreshService = new CatalogRefreshService(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<SyncOptions>>(),
            services.GetRequiredService<TimeProvider>(),
            NullLogger<CatalogRefreshService>.Instance,
            delayer: (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct)); // outer schedule idles until stopped

        await refreshService.StartAsync(CancellationToken.None);
        await WaitForAsync(() => catalogStore.Products.Count > 0);
        await refreshService.StopAsync(CancellationToken.None);

        var product = Assert.Single(catalogStore.Products);
        Assert.Equal(42, product.ExternalId);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met within 5s");
            await Task.Delay(20);
        }
    }
}
