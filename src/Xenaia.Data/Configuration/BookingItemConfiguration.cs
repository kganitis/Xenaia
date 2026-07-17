using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Data.Configuration;

public sealed class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ParticipantTypeAlias).HasMaxLength(128);
        builder.Property(i => i.FinalPrice).HasPrecision(18, 2);
        builder.HasIndex("BookingId", nameof(BookingItem.ExternalId)).IsUnique();
    }
}
