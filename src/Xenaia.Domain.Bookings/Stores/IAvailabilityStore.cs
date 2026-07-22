using Xenaia.Domain.Bookings.Availabilities;

namespace Xenaia.Domain.Bookings.Stores;

/// <summary>Composite key for an availability row: a (product, option,
/// timeslot) triple as addressed by external ids.</summary>
public readonly record struct AvailabilityKey(
    int ProductExternalId, int OptionExternalId, DateTimeOffset TimeslotAt);

/// <summary>Repository port for the Availability aggregate. Implemented in
/// Xenaia.Data as an EF-backed scoped service.</summary>
public interface IAvailabilityStore
{
    /// <summary>Existing rows for the given (productId, optionId, timeslot)
    /// composite keys, tracked, for value-aware dedup.</summary>
    Task<IReadOnlyList<Availability>> GetByKeysAsync(
        IReadOnlyCollection<AvailabilityKey> keys, CancellationToken ct);
    Task AddAsync(Availability availability, CancellationToken ct);
    /// <summary>Atomic Pending -> Processing claim; false if already claimed.</summary>
    Task<bool> TryClaimAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<Availability>> GetPendingAsync(int batchSize, CancellationToken ct);
    /// <summary>Startup recovery: stuck Processing back to Pending.</summary>
    Task<int> ResetProcessingAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
