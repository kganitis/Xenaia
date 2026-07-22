using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Stores;

/// <summary>Repository port for the durable outbound-booking-request queue.
/// Implemented in Xenaia.Data as an EF-backed scoped service.</summary>
public interface IOutboundBookingRequestStore
{
    Task AddAsync(OutboundBookingRequest request, CancellationToken ct);
    Task<bool> TryClaimAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<OutboundBookingRequest>> GetPendingAsync(
        int batchSize, CancellationToken ct);
    Task<int> ResetProcessingAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
