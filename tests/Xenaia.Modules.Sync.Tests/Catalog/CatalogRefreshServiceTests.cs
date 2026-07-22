using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Products;
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

    /// <summary>Regression test for a bug where a failed refresh looped back
    /// to the top-of-schedule wait instead of retrying directly: that wait
    /// recomputes "next scheduled day" (today's instant already passed, or
    /// this refresh would not have run), silently stretching "retry in 1
    /// hour" out to nearly a full day. The catalog starts non-empty so warm
    /// start is skipped; the vendor fails its first call once, so the
    /// scheduled attempt fails, and the fix must retry directly (no second
    /// _delayer call) rather than waiting for the next scheduled instant.</summary>
    [Fact]
    public async Task A_failed_refresh_retries_directly_after_one_hour_not_via_the_next_scheduled_day()
    {
        var existing = Product.Define(1, "Sunset Kayak Tour");
        existing.ClaimForSync();
        existing.MarkSynced(new DateTimeOffset(2026, 7, 31, 3, 0, 0, TimeSpan.Zero));
        var catalogStore = new FakeCatalogStore();
        catalogStore.SeedProduct(existing); // non-empty: warm start is skipped

        var providerFake = new InMemoryBookingSystemProvider();
        providerFake.SeedProduct(new ProductSnapshot(2, "Evening Cooking Class", null));
        providerFake.FailNextCallWith = new InvalidOperationException("vendor unavailable"); // fires once

        var services = new ServiceCollection()
            .AddSingleton<IBookingSystemProvider>(providerFake)
            .AddSingleton<ICatalogStore>(catalogStore)
            .AddSingleton(Options.Create(new SyncOptions()))
            .AddSingleton<ILogger<CatalogSyncService>>(NullLogger<CatalogSyncService>.Instance)
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ParticipantTypeCache>()
            .AddScoped<CatalogSyncService>()
            .BuildServiceProvider();

        var delays = new List<TimeSpan>();
        var recordedDelays = 0;
        var refreshService = new CatalogRefreshService(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<SyncOptions>>(),
            services.GetRequiredService<TimeProvider>(),
            NullLogger<CatalogRefreshService>.Instance,
            delayer: (delay, ct) =>
            {
                lock (delays) delays.Add(delay);
                // Freezes after the 3rd delay (schedule wait, retry wait,
                // and - only under the bug - a second schedule wait) so the
                // test settles into an observable, stoppable state either way.
                return Interlocked.Increment(ref recordedDelays) >= 3
                    ? Task.Delay(Timeout.InfiniteTimeSpan, ct)
                    : Task.CompletedTask;
            });

        await refreshService.StartAsync(CancellationToken.None);
        await WaitForAsync(() => catalogStore.Products.Count == 2 || Volatile.Read(ref recordedDelays) >= 3);
        await refreshService.StopAsync(CancellationToken.None);

        // Fixed behavior: the retry runs immediately after the 1-hour wait
        // (no interposed delay), so the vendor's second product is already
        // synced by the time a 3rd delay (if any) would appear.
        Assert.Equal(2, catalogStore.Products.Count);
        Assert.True(delays.Count >= 2);
        Assert.Equal(TimeSpan.FromHours(1), delays[1]);
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
