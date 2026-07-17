using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Catalog;

namespace Xenaia.Data.Configuration;

public sealed class ParticipantTypeConfiguration : IEntityTypeConfiguration<ParticipantType>
{
    public void Configure(EntityTypeBuilder<ParticipantType> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Alias).HasMaxLength(128);
        builder.Property(p => p.Title).HasMaxLength(256);
        builder.HasIndex(
                nameof(ParticipantType.ProductOptionId),
                nameof(ParticipantType.Alias))
            .IsUnique();

        builder.ComplexProperty(p => p.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
