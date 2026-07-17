using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Products;

namespace Xenaia.Data.Configuration;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.ExternalId).IsUnique();

        builder.Property(p => p.Code)
            .HasConversion(code => code!.Value, value => ProductCode.FromTrusted(value))
            .HasMaxLength(64);
        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.Title).HasMaxLength(256);

        builder.ComplexProperty(p => p.Sync, SyncStateMapping.Configure);

        builder.HasMany(p => p.Options).WithOne()
            .HasForeignKey("ProductId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Options)
            .HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);

        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
