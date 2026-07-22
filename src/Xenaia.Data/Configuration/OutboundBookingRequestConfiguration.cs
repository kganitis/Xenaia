using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Configuration;

/// <summary>
/// The durable outbound-booking-request queue. Mirrors
/// <see cref="AvailabilityConfiguration"/>: the same SyncState column layout,
/// audit stamps, and xmin concurrency token, so a claim races cleanly.
/// </summary>
public sealed class OutboundBookingRequestConfiguration
    : IEntityTypeConfiguration<OutboundBookingRequest>
{
    public void Configure(EntityTypeBuilder<OutboundBookingRequest> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Kind).HasConversion<int>();
        builder.Property(r => r.Payload).HasColumnType("text");

        builder.ComplexProperty(r => r.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
