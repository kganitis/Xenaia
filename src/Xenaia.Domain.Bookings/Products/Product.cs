using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Products;

/// <summary>
/// Aggregate root for a sellable product and its options. The catalog code
/// is optional until an adapter or operator assigns one (T5).
/// </summary>
public sealed class Product : AggregateRoot<int>, ISyncTracked
{
    private readonly List<ProductOption> _options = [];

    public int ExternalId { get; private set; }

    public ProductCode? Code { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public int? CategoryId { get; private set; }

    public SyncState Sync { get; private set; } = SyncState.Pending;

    public IReadOnlyCollection<ProductOption> Options => _options.AsReadOnly();

    private Product(int id) : base(id) { }

    public static Product Define(int externalId, string title, int? categoryId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ProductRuleViolationException("Product title cannot be blank.");

        return new Product(0)
        {
            ExternalId = externalId,
            Title = title,
            CategoryId = categoryId,
        };
    }

    public void AssignCode(ProductCode code) => Code = code;

    public void Retitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ProductRuleViolationException("Product title cannot be blank.");
        Title = title;
    }

    public void Recategorize(int? categoryId) => CategoryId = categoryId;

    public ProductOption AddOption(int externalId, string title)
    {
        if (_options.Any(o => o.ExternalId == externalId))
            throw new ProductRuleViolationException(
                $"Product {ExternalId} already has an option with external id {externalId}.");

        var option = new ProductOption(externalId, title);
        _options.Add(option);
        return option;
    }

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
