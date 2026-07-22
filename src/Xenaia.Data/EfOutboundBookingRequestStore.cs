using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data;

/// <summary>EF-backed repository for the durable outbound-booking-request queue.</summary>
public sealed class EfOutboundBookingRequestStore(XenaiaDbContext context)
    : IOutboundBookingRequestStore
{
    public Task AddAsync(OutboundBookingRequest request, CancellationToken ct)
    {
        context.OutboundBookingRequests.Add(request);
        return Task.CompletedTask;
    }

    // Same atomic claim as EfAvailabilityStore: a single guarded UPDATE over
    // the complex Sync.Status member, so the claim races cleanly.
    public async Task<bool> TryClaimAsync(int id, CancellationToken ct)
    {
        var claimed = await context.OutboundBookingRequests
            .Where(r => r.Id == id && r.Sync.Status == SyncStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Sync.Status, SyncStatus.Processing), ct);
        return claimed == 1;
    }

    public async Task<IReadOnlyList<OutboundBookingRequest>> GetPendingAsync(
        int batchSize, CancellationToken ct)
        => await context.OutboundBookingRequests
            .Where(r => r.Sync.Status == SyncStatus.Pending)
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<int> ResetProcessingAsync(CancellationToken ct)
        => await context.OutboundBookingRequests
            .Where(r => r.Sync.Status == SyncStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Sync.Status, SyncStatus.Pending), ct);

    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
