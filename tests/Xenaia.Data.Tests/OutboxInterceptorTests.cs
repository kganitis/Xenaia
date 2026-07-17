using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Bookings.Events;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class OutboxInterceptorTests(PostgresFixture fixture)
{
    private static readonly CodeFormat Format = CodeFormat.Create("^MT-[A-Z0-9]{8}$");
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private static Booking NewBooking(string code) => Booking.Receive(
        BookingCode.Create(code, Format), "s", BookingType.Api,
        BookingStatus.Pending, 10m, SyncDirection.Inbound, At);

    [Fact]
    public async Task Saving_a_received_booking_appends_a_round_trippable_outbox_message()
    {
        await using var context = fixture.CreateContext();
        context.Bookings.Add(NewBooking("MT-EVENT001"));

        await context.SaveChangesAsync();

        var message = await context.Outbox.SingleAsync(
            m => m.Payload.Contains("MT-EVENT001") && m.Type.Contains(nameof(BookingReceived)));
        var evt = Assert.IsType<BookingReceived>(message.ToDomainEvent());
        Assert.Equal("MT-EVENT001", evt.Code);
        Assert.Equal(At, evt.OccurredAt);
    }

    [Fact]
    public async Task Events_are_drained_so_a_second_save_does_not_duplicate_them()
    {
        await using var context = fixture.CreateContext();
        var booking = NewBooking("MT-EVENT002");
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        booking.SetActivityLanguage("en");
        await context.SaveChangesAsync();

        Assert.Single(await context.Outbox
            .Where(m => m.Payload.Contains("MT-EVENT002")).ToListAsync());
    }

    [Fact]
    public async Task Audit_stamps_are_set_on_insert_and_bumped_on_update()
    {
        await using var context = fixture.CreateContext();
        var booking = NewBooking("MT-EVENT003");
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var created = context.Entry(booking).Property<DateTimeOffset>("CreatedAt").CurrentValue;
        var updated = context.Entry(booking).Property<DateTimeOffset>("UpdatedAt").CurrentValue;
        Assert.NotEqual(default, created);
        Assert.Equal(created, updated);

        booking.SetActivityLanguage("en");
        await context.SaveChangesAsync();

        var bumped = context.Entry(booking).Property<DateTimeOffset>("UpdatedAt").CurrentValue;
        Assert.True(bumped > created);
        Assert.Equal(created, context.Entry(booking).Property<DateTimeOffset>("CreatedAt").CurrentValue);
    }
}
