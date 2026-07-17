using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Catalog;

/// <summary>A payment method supported by the external booking system.</summary>
public sealed class PaymentType : Entity<int>, ISyncTracked
{
    public string Code { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public bool IsActive { get; private set; } = true;

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private PaymentType(int id) : base(id) { }

    public static PaymentType Define(string code, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new CatalogRuleViolationException("Payment type code cannot be blank.");

        return new PaymentType(0) { Code = code, Title = title };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
