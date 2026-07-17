using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Catalog;

namespace Xenaia.Data.Configuration;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.ExternalId).IsUnique();
        builder.Property(c => c.Title).HasMaxLength(256);
        builder.Property(c => c.Description).HasMaxLength(2000);

        builder.ComplexProperty(c => c.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
