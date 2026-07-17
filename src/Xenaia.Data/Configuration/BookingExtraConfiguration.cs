using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Data.Configuration;

public sealed class BookingExtraConfiguration : IEntityTypeConfiguration<BookingExtra>
{
    public void Configure(EntityTypeBuilder<BookingExtra> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ExtraAlias).HasMaxLength(128);
        builder.Property(e => e.Title).HasMaxLength(256);
        builder.Property(e => e.FinalPrice).HasPrecision(18, 2);
        builder.HasIndex("BookingId", nameof(BookingExtra.ExternalId)).IsUnique();
    }
}
