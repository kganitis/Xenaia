using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Products;

/// <summary>
/// Links a product option to a purchasable extra from the catalog
/// (referenced by id; Extra is a standalone catalog entity).
/// </summary>
public sealed class ProductOptionExtra : Entity<int>
{
    public int ExtraId { get; private set; }

    private ProductOptionExtra(int id, int extraId) : base(id)
    {
        ExtraId = extraId;
    }

    internal ProductOptionExtra(int extraId) : base(0) => ExtraId = extraId;
}
