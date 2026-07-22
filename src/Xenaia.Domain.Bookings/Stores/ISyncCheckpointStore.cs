namespace Xenaia.Domain.Bookings.Stores;

/// <summary>Repository port for named sync checkpoints (e.g. "last pulled
/// at" watermarks). Implemented in Xenaia.Data as an EF-backed scoped
/// service.</summary>
public interface ISyncCheckpointStore
{
    Task<DateTimeOffset?> GetAsync(string name, CancellationToken ct);
    Task SetAsync(string name, DateTimeOffset value, CancellationToken ct);
}
