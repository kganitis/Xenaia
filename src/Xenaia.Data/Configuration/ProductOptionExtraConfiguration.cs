using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Products;

namespace Xenaia.Data.Configuration;

public sealed class ProductOptionExtraConfiguration : IEntityTypeConfiguration<ProductOptionExtra>
{
    public void Configure(EntityTypeBuilder<ProductOptionExtra> builder)
    {
        builder.HasKey(l => l.Id);
        builder.HasIndex("ProductOptionId", nameof(ProductOptionExtra.ExtraId)).IsUnique();
    }
}
