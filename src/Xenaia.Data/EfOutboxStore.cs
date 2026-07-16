using Microsoft.EntityFrameworkCore;
using Xenaia.Core.Outbox;

namespace Xenaia.Data;

/// <summary>
/// EF-backed outbox store. Unprocessed means ProcessedAt and Error are
/// both null: parked messages stay visible in the table but are excluded
/// from every batch until a human clears Error. Marks use ExecuteUpdate
/// (no load-modify-save round trip).
/// </summary>
public sealed class EfOutboxStore(XenaiaDbContext context) : IOutboxStore
{
    public async Task AppendAsync(IEnumerable<OutboxMessage> messages, CancellationToken ct = default)
    {
        context.Outbox.AddRange(messages);
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default)
        => await context.Outbox
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.Error == null)
            .OrderBy(m => m.OccurredAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken ct = default)
        => await context.Outbox
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, processedAt), ct);

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default)
        => await context.Outbox
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Error, error), ct);
}
