using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;

namespace Xenaia.Data.Configuration;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Code)
            .HasConversion(code => code.Value, value => BookingCode.FromTrusted(value))
            .HasMaxLength(64);
        builder.HasIndex(b => b.Code).IsUnique();

        builder.Property(b => b.SecretCode).HasMaxLength(128);
        builder.Property(b => b.Referrer).HasMaxLength(256);
        builder.Property(b => b.ChannelBookingCode).HasMaxLength(128);
        builder.Property(b => b.ActivityLanguage).HasMaxLength(32);
        builder.Property(b => b.LeadContactName).HasMaxLength(256);
        builder.Property(b => b.Email).HasMaxLength(256);
        builder.Property(b => b.Phone).HasMaxLength(64);
        builder.Property(b => b.FinalPrice).HasPrecision(18, 2);

        builder.ComplexProperty(b => b.Sync, SyncStateMapping.Configure);

        builder.HasMany(b => b.Items).WithOne()
            .HasForeignKey("BookingId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Items)
            .HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Extras).WithOne()
            .HasForeignKey("BookingId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Extras)
            .HasField("_extras").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Payments).WithOne()
            .HasForeignKey("BookingId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Payments)
            .HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.GiftCards).WithOne()
            .HasForeignKey("BookingId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.GiftCards)
            .HasField("_giftCards").UsePropertyAccessMode(PropertyAccessMode.Field);

        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
