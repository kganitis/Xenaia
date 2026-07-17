using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Data.Configuration;

public sealed class BookingPaymentConfiguration : IEntityTypeConfiguration<BookingPayment>
{
    public void Configure(EntityTypeBuilder<BookingPayment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PaymentMethod).HasMaxLength(128);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.HasIndex("BookingId", nameof(BookingPayment.ExternalId)).IsUnique();
    }
}
