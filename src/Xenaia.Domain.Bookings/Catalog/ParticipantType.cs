using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Catalog;

/// <summary>A participant classification for a product option (adult, child).</summary>
public sealed class ParticipantType : Entity<int>, ISyncTracked
{
    public int ProductOptionId { get; private set; }

    public string Alias { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private ParticipantType(int id) : base(id) { }

    public static ParticipantType Define(int productOptionId, string alias, string title)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new CatalogRuleViolationException("Participant type alias cannot be blank.");

        return new ParticipantType(0)
        {
            ProductOptionId = productOptionId,
            Alias = alias,
            Title = title,
        };
    }

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
