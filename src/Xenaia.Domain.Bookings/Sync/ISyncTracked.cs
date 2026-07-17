namespace Xenaia.Domain.Bookings.Sync;

/// <summary>
/// An entity whose row synchronizes with the external booking system.
/// Implementations delegate to SyncState; the interface exists so
/// infrastructure (concurrency mapping, future batch claiming) can treat
/// all sync-tracked types uniformly.
/// </summary>
public interface ISyncTracked
{
    SyncState Sync { get; }

    void ClaimForSync();

    void MarkSynced(DateTimeOffset at);

    void MarkSyncFailed(string error, DateTimeOffset at);

    void RequeueSync();
}
