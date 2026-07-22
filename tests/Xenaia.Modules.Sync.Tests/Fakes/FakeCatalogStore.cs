using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>Minimal in-memory ICatalogStore for the availability pusher tests:
/// participant types are seeded per external (product, option) and returned by
/// GetParticipantTypesAsync; the write/read-all members the pusher never calls
/// are left unsupported. Records how many times participant types were fetched
/// so caching can be asserted.</summary>
internal sealed class FakeCatalogStore : ICatalogStore
{
    private readonly Dictionary<(int Product, int Option), List<ParticipantType>> _byOption = [];

    public int GetParticipantTypesCallCount { get; private set; }

    /// <summary>Seeds participant type aliases for an external (product, option).</summary>
    public void Seed(int productExternalId, int optionExternalId, params string[] aliases)
    {
        _byOption[(productExternalId, optionExternalId)] =
            aliases.Select(alias => ParticipantType.Define(optionExternalId, alias, alias)).ToList();
    }

    public Task<IReadOnlyList<ParticipantType>> GetParticipantTypesAsync(
        int productExternalId, int optionExternalId, CancellationToken ct)
    {
        GetParticipantTypesCallCount++;
        IReadOnlyList<ParticipantType> result =
            _byOption.TryGetValue((productExternalId, optionExternalId), out var types) ? types : [];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task AddAsync(Product product, CancellationToken ct)
        => throw new NotSupportedException();

    public Task AddParticipantTypeAsync(ParticipantType participantType, CancellationToken ct)
        => throw new NotSupportedException();

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
