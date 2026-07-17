namespace Xenaia.Domain.Bookings.Sync;

/// <summary>
/// The sync state machine as a value: Pending -> Processing -> Synced | Failed,
/// with requeue back to Pending (recovery and re-sync). Transitions return a
/// new state or throw; the table below is the only statement of the rules.
/// </summary>
public sealed record SyncState
{
    public SyncStatus Status { get; private init; } = SyncStatus.Pending;

    public string? Error { get; private init; }

    public DateTimeOffset? SyncedAt { get; private init; }

    public static SyncState Pending { get; } = new();

    public SyncState Claim() =>
        Move(SyncStatus.Processing) with { Error = null };

    public SyncState MarkSynced(DateTimeOffset at) =>
        Move(SyncStatus.Synced) with { Error = null, SyncedAt = at };

    public SyncState MarkFailed(string error) =>
        Move(SyncStatus.Failed) with { Error = error, SyncedAt = null };

    public SyncState Requeue() =>
        Move(SyncStatus.Pending) with { Error = null, SyncedAt = null };

    private static readonly Dictionary<SyncStatus, SyncStatus[]> AllowedFrom = new()
    {
        [SyncStatus.Processing] = [SyncStatus.Pending],
        [SyncStatus.Synced] = [SyncStatus.Processing, SyncStatus.Synced],
        [SyncStatus.Failed] = [SyncStatus.Pending, SyncStatus.Processing],
        [SyncStatus.Pending] = [SyncStatus.Synced, SyncStatus.Failed, SyncStatus.Processing],
    };

    private SyncState Move(SyncStatus to) =>
        AllowedFrom[to].Contains(Status)
            ? this with { Status = to }
            : throw new InvalidSyncTransitionException(Status, to);
}
