using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Catalog;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;

namespace Xenaia.Modules.Sync.Tests.Catalog;

public class CatalogSyncServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 1, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task New_product_creates_define_options_and_participant_types_all_synced()
    {
        var providerFake = new InMemoryBookingSystemProvider();
        providerFake.SeedProduct(
            new ProductSnapshot(42, "Sunset Kayak Tour", 3),
            new ProductOptionSnapshot(7, "Two-seat kayak",
                [new ParticipantTypeSnapshot("adult", "Adult"), new ParticipantTypeSnapshot("child", "Child")]));
        var catalogStore = new FakeCatalogStore();
        var service = CreateService(providerFake, catalogStore);

        var summary = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(new CatalogSyncSummary(ProductsSeen: 1, ProductsAdded: 1, OptionsAdded: 1, ParticipantTypesAdded: 2), summary);

        var product = Assert.Single(catalogStore.Products);
        Assert.Equal(42, product.ExternalId);
        Assert.Equal("Sunset Kayak Tour", product.Title);
        Assert.Equal(3, product.CategoryId);
        Assert.Equal(SyncStatus.Synced, product.Sync.Status);

        var option = Assert.Single(product.Options);
        Assert.Equal(7, option.ExternalId);
        Assert.Equal("Two-seat kayak", option.Title);
        Assert.NotEqual(0, option.Id);

        var types = await catalogStore.GetParticipantTypesAsync(42, 7, CancellationToken.None);
        Assert.Equal(2, types.Count);
        Assert.All(types, t => Assert.Equal(SyncStatus.Synced, t.Sync.Status));
        Assert.Contains(types, t => t.Alias == "adult");
        Assert.Contains(types, t => t.Alias == "child");
    }

    [Fact]
    public async Task Existing_product_is_retitled_recategorized_gains_a_new_option_and_leaves_existing_options_untouched()
    {
        var existing = Product.Define(42, "Sunset Kayak Tour", categoryId: 3);
        existing.AddOption(7, "Two-seat kayak");
        existing.ClaimForSync();
        existing.MarkSynced(FixedNow.AddDays(-1));
        var catalogStore = new FakeCatalogStore();
        catalogStore.SeedProduct(existing);

        var providerFake = new InMemoryBookingSystemProvider();
        providerFake.SeedProduct(
            new ProductSnapshot(42, "Sunset Kayak Adventure", 9),
            new ProductOptionSnapshot(7, "Renamed upstream (ignored)", []),   // existing option: title must not change
            new ProductOptionSnapshot(11, "Single-seat kayak", []));         // brand new option
        var service = CreateService(providerFake, catalogStore);

        var summary = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(0, summary.ProductsAdded);
        Assert.Equal(1, summary.OptionsAdded);

        var product = Assert.Single(catalogStore.Products);
        Assert.Equal("Sunset Kayak Adventure", product.Title);
        Assert.Equal(9, product.CategoryId);
        Assert.Equal(SyncStatus.Synced, product.Sync.Status);

        Assert.Equal(2, product.Options.Count);
        var untouchedOption = product.Options.Single(o => o.ExternalId == 7);
        Assert.Equal("Two-seat kayak", untouchedOption.Title);            // untouched, not "Renamed upstream"
        var newOption = product.Options.Single(o => o.ExternalId == 11);
        Assert.Equal("Single-seat kayak", newOption.Title);
    }

    [Fact]
    public async Task Existing_participant_type_is_not_duplicated_matched_by_alias_per_option()
    {
        var existing = Product.Define(42, "Sunset Kayak Tour");
        existing.AddOption(7, "Two-seat kayak");
        existing.ClaimForSync();
        existing.MarkSynced(FixedNow.AddDays(-1));
        var catalogStore = new FakeCatalogStore();
        catalogStore.SeedProduct(existing);
        catalogStore.Seed(42, 7, "adult"); // already-synced participant type

        var providerFake = new InMemoryBookingSystemProvider();
        providerFake.SeedProduct(
            new ProductSnapshot(42, "Sunset Kayak Tour", null),
            new ProductOptionSnapshot(7, "Two-seat kayak",
                [new ParticipantTypeSnapshot("adult", "Adult"), new ParticipantTypeSnapshot("child", "Child")]));
        var service = CreateService(providerFake, catalogStore);

        var summary = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, summary.ParticipantTypesAdded); // only "child" is new

        var types = await catalogStore.GetParticipantTypesAsync(42, 7, CancellationToken.None);
        Assert.Equal(2, types.Count);
        Assert.Single(types, t => t.Alias == "adult");
        Assert.Single(types, t => t.Alias == "child");
    }

    [Fact]
    public async Task Throttle_delay_is_called_between_products_but_not_before_the_first()
    {
        var providerFake = new InMemoryBookingSystemProvider();
        providerFake.SeedProduct(new ProductSnapshot(1, "Sunset Kayak Tour", null));
        providerFake.SeedProduct(new ProductSnapshot(2, "Evening Cooking Class", null));
        providerFake.SeedProduct(new ProductSnapshot(3, "Guided Walk", null));
        var catalogStore = new FakeCatalogStore();
        var delays = new List<TimeSpan>();
        var service = CreateService(
            providerFake, catalogStore,
            delayer: (delay, ct) => { delays.Add(delay); return Task.CompletedTask; });

        var summary = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(3, summary.ProductsSeen);
        Assert.Equal(2, delays.Count); // 2 delays between 3 products
        Assert.All(delays, d => Assert.Equal(TimeSpan.FromMilliseconds(1000), d));
    }

    private static CatalogSyncService CreateService(
        InMemoryBookingSystemProvider provider,
        FakeCatalogStore catalogStore,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        var options = Options.Create(new SyncOptions());
        var clock = new FakeTimeProvider(FixedNow);
        return new CatalogSyncService(
            provider, catalogStore, CreateParticipantTypeCache(catalogStore),
            options, clock, NullLogger<CatalogSyncService>.Instance,
            delayer ?? ((_, _) => Task.CompletedTask));
    }

    private static ParticipantTypeCache CreateParticipantTypeCache(FakeCatalogStore catalogStore)
    {
        var provider = new ServiceCollection()
            .AddSingleton<ICatalogStore>(catalogStore)
            .BuildServiceProvider();
        return new ParticipantTypeCache(provider.GetRequiredService<IServiceScopeFactory>());
    }
}
