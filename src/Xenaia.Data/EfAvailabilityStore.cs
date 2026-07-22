using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Availabilities;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data;

/// <summary>EF-backed repository for the Availability aggregate.</summary>
public sealed class EfAvailabilityStore(XenaiaDbContext context) : IAvailabilityStore
{
    public async Task<IReadOnlyList<Availability>> GetByKeysAsync(
        IReadOnlyCollection<AvailabilityKey> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
            return [];

        // Server-side narrowing by the three key components, then an exact
        // composite match in memory (the three IN lists over-fetch a little,
        // but keep the query translatable and index-friendly).
        var productIds = keys.Select(k => k.ProductExternalId).ToHashSet();
        var optionIds = keys.Select(k => k.OptionExternalId).ToHashSet();
        var timeslots = keys.Select(k => k.TimeslotAt).ToHashSet();

        var candidates = await context.Availabilities
            .Where(a => productIds.Contains(a.ExternalProductId)
                && optionIds.Contains(a.ExternalOptionId)
                && timeslots.Contains(a.TimeslotAt))
            .ToListAsync(ct);

        var wanted = keys.ToHashSet();
        return candidates
            .Where(a => wanted.Contains(
                new AvailabilityKey(a.ExternalProductId, a.ExternalOptionId, a.TimeslotAt)))
            .ToList();
    }

    public async Task<Availability?> GetByIdAsync(int id, CancellationToken ct)
        => await context.Availabilities.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task AddAsync(Availability availability, CancellationToken ct)
    {
        context.Availabilities.Add(availability);
        return Task.CompletedTask;
    }

    // Atomic Pending -> Processing: one UPDATE ... WHERE id = @id AND
    // sync_status = Pending. ExecuteUpdateAsync over the complex Sync.Status
    // member compiles to a single round trip, so two workers cannot both claim
    // the same row (the loser's UPDATE matches zero rows).
    public async Task<bool> TryClaimAsync(int id, CancellationToken ct)
    {
        var claimed = await context.Availabilities
            .Where(a => a.Id == id && a.Sync.Status == SyncStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Sync.Status, SyncStatus.Processing), ct);
        return claimed == 1;
    }

    public async Task<IReadOnlyList<Availability>> GetPendingAsync(int batchSize, CancellationToken ct)
        => await context.Availabilities
            .Where(a => a.Sync.Status == SyncStatus.Pending)
            .OrderBy(a => a.Id)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<int> ResetProcessingAsync(CancellationToken ct)
        => await context.Availabilities
            .Where(a => a.Sync.Status == SyncStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Sync.Status, SyncStatus.Pending), ct);

    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
