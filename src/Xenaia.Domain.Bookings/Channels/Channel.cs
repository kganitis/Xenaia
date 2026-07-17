using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Channels;

/// <summary>A sales or distribution channel bookings arrive through.</summary>
public sealed class Channel : AggregateRoot<int>, ISyncTracked
{
    public string Code { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public bool IsActive { get; private set; } = true;

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private Channel(int id) : base(id) { }

    public static Channel Define(string code, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ChannelRuleViolationException("Channel code cannot be blank.");

        return new Channel(0) { Code = code, Title = title };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void Retitle(string? title) => Title = title;

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
