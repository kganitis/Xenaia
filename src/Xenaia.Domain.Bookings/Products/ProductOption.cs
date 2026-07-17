using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Products;

/// <summary>
/// A bookable variant of a product. Created only through Product.AddOption;
/// no sync state of its own (it rides on its root).
/// </summary>
public sealed class ProductOption : Entity<int>
{
    private readonly List<ProductOptionExtra> _extraLinks = [];

    public int ExternalId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public IReadOnlyCollection<ProductOptionExtra> ExtraLinks => _extraLinks.AsReadOnly();

    private ProductOption(int id) : base(id) { }

    internal ProductOption(int externalId, string title) : base(0)
    {
        ExternalId = externalId;
        Title = title;
    }

    public ProductOptionExtra LinkExtra(int extraId)
    {
        if (_extraLinks.Any(l => l.ExtraId == extraId))
            throw new ProductRuleViolationException(
                $"Option {ExternalId} already links extra {extraId}.");

        var link = new ProductOptionExtra(extraId);
        _extraLinks.Add(link);
        return link;
    }
}
