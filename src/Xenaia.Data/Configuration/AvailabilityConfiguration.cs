using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Availabilities;

namespace Xenaia.Data.Configuration;

public sealed class AvailabilityConfiguration : IEntityTypeConfiguration<Availability>
{
    public void Configure(EntityTypeBuilder<Availability> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(
                nameof(Availability.ExternalProductId),
                nameof(Availability.ExternalOptionId),
                nameof(Availability.TimeslotAt))
            .IsUnique();

        builder.ComplexProperty(a => a.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
