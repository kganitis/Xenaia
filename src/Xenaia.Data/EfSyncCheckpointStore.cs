using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Data;

/// <summary>EF-backed repository for named sync checkpoints.</summary>
public sealed class EfSyncCheckpointStore(XenaiaDbContext context) : ISyncCheckpointStore
{
    public async Task<DateTimeOffset?> GetAsync(string name, CancellationToken ct)
    {
        var row = await context.SyncCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Name == name, ct);
        return row?.Value;
    }

    public async Task SetAsync(string name, DateTimeOffset value, CancellationToken ct)
    {
        var existing = await context.SyncCheckpoints
            .SingleOrDefaultAsync(c => c.Name == name, ct);
        if (existing is null)
            context.SyncCheckpoints.Add(new SyncCheckpoint { Name = name, Value = value });
        else
            existing.Value = value;
        await context.SaveChangesAsync(ct);
    }
}
