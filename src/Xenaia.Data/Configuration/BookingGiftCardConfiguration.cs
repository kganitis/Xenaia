using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Data.Configuration;

public sealed class BookingGiftCardConfiguration : IEntityTypeConfiguration<BookingGiftCard>
{
    public void Configure(EntityTypeBuilder<BookingGiftCard> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Code).HasMaxLength(128);
        builder.Property(g => g.Amount).HasPrecision(18, 2);
        builder.HasIndex("BookingId", nameof(BookingGiftCard.Code)).IsUnique();
    }
}
