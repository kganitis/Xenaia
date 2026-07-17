using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Catalog;

namespace Xenaia.Data.Configuration;

public sealed class PaymentTypeConfiguration : IEntityTypeConfiguration<PaymentType>
{
    public void Configure(EntityTypeBuilder<PaymentType> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(128);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Title).HasMaxLength(256);

        builder.ComplexProperty(p => p.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
