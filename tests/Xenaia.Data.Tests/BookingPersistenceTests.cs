using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class BookingPersistenceTests(PostgresFixture fixture)
{
    private static readonly CodeFormat Format = CodeFormat.Create("^MT-[A-Z0-9]{8}$");
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private static Booking NewBooking(string code)
    {
        var booking = Booking.Receive(
            BookingCode.Create(code, Format),
            secretCode: "secret-1",
            type: BookingType.Api,
            status: BookingStatus.Pending,
            finalPrice: 180.50m,
            direction: SyncDirection.Inbound,
            occurredAt: At);
        booking.UpdateContact("Alex Rivera", "alex@example.com", "+1-555-0100");
        booking.AddItem(501, 42, 7, "adult", At.AddDays(3), 120m);
        booking.AddItem(502, 42, 7, "child", At.AddDays(3), 60.50m);
        booking.AddExtra(801, 7, "lunch", "Picnic lunch", At.AddDays(3), 2, 30m);
        booking.RecordPayment(9001, 180.50m, "card", PaymentStatus.Captured, At);
        booking.ApplyGiftCard("MTGIFT-01", 25m);
        return booking;
    }

    [Fact]
    public async Task Booking_round_trips_with_children_and_value_objects()
    {
        var booking = NewBooking("MT-7KQ2XY9A");
        booking.ClaimForSync();
        booking.MarkSyncFailed("provider unreachable", At);
        booking.DequeueDomainEvents();

        await using (var write = fixture.CreateContext())
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var loaded = await read.Bookings
            .Include(b => b.Items)
            .Include(b => b.Extras)
            .Include(b => b.Payments)
            .Include(b => b.GiftCards)
            .SingleAsync(b => b.Code == BookingCode.FromTrusted("MT-7KQ2XY9A"));

        Assert.Equal(booking.Code, loaded.Code);
        Assert.Equal("Alex Rivera", loaded.LeadContactName);
        Assert.Equal(180.50m, loaded.FinalPrice);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Single(loaded.Extras);
        Assert.Single(loaded.Payments);
        Assert.Single(loaded.GiftCards);
        Assert.Equal(SyncStatus.Failed, loaded.Sync.Status);
        Assert.Equal("provider unreachable", loaded.Sync.Error);
        Assert.Equal(60.50m, loaded.Items.Single(i => i.ExternalId == 502).FinalPrice);
    }

    [Fact]
    public async Task Duplicate_booking_codes_are_rejected_by_the_database()
    {
        await using var context = fixture.CreateContext();
        context.Bookings.Add(NewBooking("MT-DUP00001"));
        await context.SaveChangesAsync();

        await using var second = fixture.CreateContext();
        second.Bookings.Add(NewBooking("MT-DUP00001"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
