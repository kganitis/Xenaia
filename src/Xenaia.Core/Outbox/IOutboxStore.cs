namespace Xenaia.Core.Outbox;

/// <summary>Persistence port for the transactional outbox. EF implementation lands with the Data layer.</summary>
public interface IOutboxStore
{
    Task AppendAsync(IEnumerable<OutboxMessage> messages, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken ct = default);
    Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default);
}
