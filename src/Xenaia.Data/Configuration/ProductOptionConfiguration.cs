using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Products;

namespace Xenaia.Data.Configuration;

public sealed class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Title).HasMaxLength(256);
        builder.HasIndex("ProductId", nameof(ProductOption.ExternalId)).IsUnique();

        builder.HasMany(o => o.ExtraLinks).WithOne()
            .HasForeignKey("ProductOptionId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.ExtraLinks)
            .HasField("_extraLinks").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
