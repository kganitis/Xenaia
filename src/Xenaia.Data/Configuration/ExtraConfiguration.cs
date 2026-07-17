using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Catalog;

namespace Xenaia.Data.Configuration;

public sealed class ExtraConfiguration : IEntityTypeConfiguration<Extra>
{
    public void Configure(EntityTypeBuilder<Extra> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Alias).HasMaxLength(128);
        builder.HasIndex(e => e.Alias).IsUnique();
        builder.Property(e => e.Title).HasMaxLength(256);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Price).HasPrecision(18, 2);

        builder.ComplexProperty(e => e.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
