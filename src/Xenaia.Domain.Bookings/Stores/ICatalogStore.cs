using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Products;

namespace Xenaia.Domain.Bookings.Stores;

/// <summary>Repository port for the Product/ParticipantType catalog.
/// Implemented in Xenaia.Data as an EF-backed scoped service.</summary>
public interface ICatalogStore
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct);  // tracked, options only (participant types via GetParticipantTypesAsync)
    Task AddAsync(Product product, CancellationToken ct);
    Task AddParticipantTypeAsync(ParticipantType participantType, CancellationToken ct);
    /// <summary>Participant types for an option addressed by external ids
    /// (the store resolves external to internal option identity).</summary>
    Task<IReadOnlyList<ParticipantType>> GetParticipantTypesAsync(
        int productExternalId, int optionExternalId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
