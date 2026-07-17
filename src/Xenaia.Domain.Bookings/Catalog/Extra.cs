using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Catalog;

/// <summary>A purchasable add-on from the catalog (lunch, transfer, gear).</summary>
public sealed class Extra : Entity<int>, ISyncTracked
{
    public string Alias { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal? Price { get; private set; }

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private Extra(int id) : base(id) { }

    public static Extra Define(
        string alias, string title, string? description = null, decimal? price = null)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new CatalogRuleViolationException("Extra alias cannot be blank.");
        if (price is < 0)
            throw new CatalogRuleViolationException("Extra price cannot be negative.");

        return new Extra(0)
        {
            Alias = alias,
            Title = title,
            Description = description,
            Price = price,
        };
    }

    public void Reprice(decimal? price)
    {
        if (price is < 0)
            throw new CatalogRuleViolationException("Extra price cannot be negative.");
        Price = price;
    }

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
