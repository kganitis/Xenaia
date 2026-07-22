using System.Reflection;
using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>In-memory ICatalogStore. Mimics EF's deferred key generation
/// (like FakeAvailabilityStore): AddAsync/AddParticipantTypeAsync buffer the
/// entity without an id until SaveChangesAsync assigns the incremental id
/// and commits it. A new option gains its internal id the same way, whether
/// it arrived on a newly added product or was appended to an already-seeded
/// one, so CatalogSyncService can rely on ProductOption.Id being populated
/// only after a save (spec 6.5's "internal id assigned on save" contract).
/// Participant types added through AddParticipantTypeAsync are resolved back
/// to their external (product, option) pair via an internal-option-id map so
/// GetParticipantTypesAsync (addressed by external ids, as the real store
/// requires) sees them once committed. Records call counts for
/// GetParticipantTypesAsync and SaveChangesAsync so caching can be asserted.</summary>
internal sealed class FakeCatalogStore : ICatalogStore
{
    private readonly Dictionary<(int Product, int Option), List<ParticipantType>> _byOption = [];
    private readonly Dictionary<int, Product> _productsByExternalId = [];
    private readonly Dictionary<int, (int ProductExternalId, int OptionExternalId)> _optionKeysByInternalId = [];
    private readonly List<Product> _pendingProducts = [];
    private readonly List<ParticipantType> _pendingParticipantTypes = [];
    private int _nextProductId = 1;
    private int _nextOptionId = 1;
    private int _nextParticipantTypeId = 1;

    public int GetParticipantTypesCallCount { get; private set; }

    public int GetProductsCallCount { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    /// <summary>Committed products, for test assertions.</summary>
    public IReadOnlyList<Product> Products => [.. _productsByExternalId.Values];

    /// <summary>Seeds aliases for an external (product, option), as if a
    /// prior sync had already persisted those participant types.</summary>
    public void Seed(int productExternalId, int optionExternalId, params string[] aliases)
    {
        _byOption[(productExternalId, optionExternalId)] =
            aliases.Select(alias => ParticipantType.Define(optionExternalId, alias, alias)).ToList();
    }

    /// <summary>Seeds a pre-existing product (as though a prior sync already
    /// persisted it), assigning internal ids to the product and each of its
    /// options and registering the external-id mapping AddParticipantTypeAsync
    /// resolves its internal option id through.</summary>
    public Product SeedProduct(Product product)
    {
        AssignId(product, _nextProductId++);
        foreach (var option in product.Options)
        {
            AssignId(option, _nextOptionId++);
            _optionKeysByInternalId[option.Id] = (product.ExternalId, option.ExternalId);
        }
        _productsByExternalId[product.ExternalId] = product;
        return product;
    }

    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct)
    {
        GetProductsCallCount++;
        IReadOnlyList<Product> result = [.. _productsByExternalId.Values];
        return Task.FromResult(result);
    }

    public Task AddAsync(Product product, CancellationToken ct)
    {
        // No id, not yet committed: matches EF, where an Added-but-unsaved
        // entity has no database-generated key and is not query-visible.
        _pendingProducts.Add(product);
        return Task.CompletedTask;
    }

    public Task AddParticipantTypeAsync(ParticipantType participantType, CancellationToken ct)
    {
        _pendingParticipantTypes.Add(participantType);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ParticipantType>> GetParticipantTypesAsync(
        int productExternalId, int optionExternalId, CancellationToken ct)
    {
        GetParticipantTypesCallCount++;
        IReadOnlyList<ParticipantType> result =
            _byOption.TryGetValue((productExternalId, optionExternalId), out var types) ? types : [];
        return Task.FromResult(result);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        foreach (var product in _pendingProducts)
        {
            AssignId(product, _nextProductId++);
            _productsByExternalId[product.ExternalId] = product;
        }
        _pendingProducts.Clear();

        // Any option (new or on an already-committed product) that has not
        // yet been assigned an internal id gets one now, and is registered
        // in the external-id map used to resolve participant types.
        foreach (var product in _productsByExternalId.Values)
        {
            foreach (var option in product.Options)
            {
                if (option.Id == 0)
                    AssignId(option, _nextOptionId++);
                _optionKeysByInternalId[option.Id] = (product.ExternalId, option.ExternalId);
            }
        }

        foreach (var participantType in _pendingParticipantTypes)
        {
            AssignId(participantType, _nextParticipantTypeId++);
            if (!_optionKeysByInternalId.TryGetValue(participantType.ProductOptionId, out var key))
                continue;

            if (!_byOption.TryGetValue(key, out var list))
            {
                list = [];
                _byOption[key] = list;
            }
            list.Add(participantType);
        }
        _pendingParticipantTypes.Clear();

        SaveChangesCallCount++;
        return Task.CompletedTask;
    }

    private static void AssignId(Entity<int> entity, int id)
    {
        var field = typeof(Entity<int>).GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Could not locate Entity<int>'s Id backing field for test id assignment.");
        field.SetValue(entity, id);
    }
}
