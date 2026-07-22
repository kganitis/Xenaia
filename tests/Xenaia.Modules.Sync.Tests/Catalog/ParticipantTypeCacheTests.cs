using Microsoft.Extensions.DependencyInjection;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Modules.Sync.Catalog;
using Xenaia.Modules.Sync.Tests.Fakes;

namespace Xenaia.Modules.Sync.Tests.Catalog;

public class ParticipantTypeCacheTests
{
    [Fact]
    public async Task Second_call_for_the_same_key_hits_the_cache_not_the_store()
    {
        var catalogStore = new FakeCatalogStore();
        catalogStore.Seed(42, 7, "adult", "child");
        var cache = CreateCache(catalogStore);

        var first = await cache.GetAliasesAsync(42, 7, CancellationToken.None);
        var second = await cache.GetAliasesAsync(42, 7, CancellationToken.None);

        Assert.Equal(["adult", "child"], first);
        Assert.Equal(["adult", "child"], second);
        Assert.Equal(1, catalogStore.GetParticipantTypesCallCount);
    }

    [Fact]
    public async Task Invalidate_forces_a_re_read_on_the_next_call()
    {
        var catalogStore = new FakeCatalogStore();
        catalogStore.Seed(42, 7, "adult");
        var cache = CreateCache(catalogStore);

        await cache.GetAliasesAsync(42, 7, CancellationToken.None);
        cache.Invalidate();
        await cache.GetAliasesAsync(42, 7, CancellationToken.None);

        Assert.Equal(2, catalogStore.GetParticipantTypesCallCount);
    }

    [Fact]
    public async Task Different_keys_are_cached_independently()
    {
        var catalogStore = new FakeCatalogStore();
        catalogStore.Seed(42, 7, "adult");
        catalogStore.Seed(42, 8, "child");
        var cache = CreateCache(catalogStore);

        var forOptionSeven = await cache.GetAliasesAsync(42, 7, CancellationToken.None);
        var forOptionEight = await cache.GetAliasesAsync(42, 8, CancellationToken.None);

        Assert.Equal(["adult"], forOptionSeven);
        Assert.Equal(["child"], forOptionEight);
        Assert.Equal(2, catalogStore.GetParticipantTypesCallCount);
    }

    private static ParticipantTypeCache CreateCache(FakeCatalogStore catalogStore)
    {
        var provider = new ServiceCollection()
            .AddSingleton<ICatalogStore>(catalogStore)
            .BuildServiceProvider();
        return new ParticipantTypeCache(provider.GetRequiredService<IServiceScopeFactory>());
    }
}
