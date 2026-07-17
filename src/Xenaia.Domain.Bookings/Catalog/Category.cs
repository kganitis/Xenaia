using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Catalog;

/// <summary>A product grouping from the booking system's catalog.</summary>
public sealed class Category : Entity<int>, ISyncTracked
{
    public int ExternalId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private Category(int id) : base(id) { }

    public static Category Define(int externalId, string title, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new CatalogRuleViolationException("Category title cannot be blank.");

        return new Category(0)
        {
            ExternalId = externalId,
            Title = title,
            Description = description,
        };
    }

    public void Retitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new CatalogRuleViolationException("Category title cannot be blank.");
        Title = title;
    }

    public void Redescribe(string? description) => Description = description;

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
