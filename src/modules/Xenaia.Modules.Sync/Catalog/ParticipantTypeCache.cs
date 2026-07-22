using Microsoft.Extensions.DependencyInjection;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Modules.Sync.Catalog;

/// <summary>Singleton read-through cache over ICatalogStore participant
/// types, invalidated by CatalogSyncService.RefreshAsync completion. Keyed
/// by the external (product, option) pair the availability flows already
/// address participant types by. A fresh scope resolves ICatalogStore per
/// miss (the store itself is scoped), so the cache never outlives a scope
/// it did not create.</summary>
public sealed class ParticipantTypeCache(IServiceScopeFactory scopeFactory)
{
    private readonly Dictionary<(int Product, int Option), IReadOnlyList<string>> _aliases = [];
    private readonly Lock _lock = new();

    public async Task<IReadOnlyList<string>> GetAliasesAsync(
        int productExternalId, int optionExternalId, CancellationToken ct)
    {
        var key = (productExternalId, optionExternalId);
        lock (_lock)
        {
            if (_aliases.TryGetValue(key, out var cached))
                return cached;
        }

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var types = await store.GetParticipantTypesAsync(productExternalId, optionExternalId, ct);
        IReadOnlyList<string> aliases = types.Select(t => t.Alias).ToList();

        lock (_lock)
        {
            _aliases[key] = aliases;
        }
        return aliases;
    }

    /// <summary>Drops every cached entry; the next GetAliasesAsync for any
    /// key re-reads the store.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _aliases.Clear();
        }
    }
}
