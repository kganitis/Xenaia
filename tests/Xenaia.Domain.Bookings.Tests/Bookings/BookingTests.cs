using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Bookings.Events;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Bookings;

public class BookingTests
{
    private static readonly CodeFormat Format = CodeFormat.Create("^MT-[A-Z0-9]{8}$");
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private static Booking Receive() => Booking.Receive(
        BookingCode.Create("MT-7KQ2XY9Z", Format),
        secretCode: "secret-1",
        type: BookingType.Api,
        status: BookingStatus.Pending,
        finalPrice: 180.50m,
        direction: SyncDirection.Inbound,
        occurredAt: At);

    [Fact]
    public void Receive_raises_BookingReceived_and_starts_pending_sync()
    {
        var booking = Receive();

        var evt = Assert.IsType<BookingReceived>(Assert.Single(booking.DomainEvents));
        Assert.Equal("MT-7KQ2XY9Z", evt.Code);
        Assert.Equal(At, evt.OccurredAt);
        Assert.Equal(SyncStatus.Pending, booking.Sync.Status);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public void Receive_rejects_negative_price_and_cancelled_status()
    {
        var code = BookingCode.Create("MT-7KQ2XY9Z", Format);

        Assert.Throws<BookingRuleViolationException>(() => Booking.Receive(
            code, "s", BookingType.Api, BookingStatus.Pending, -1m, SyncDirection.Inbound, At));
        Assert.Throws<BookingRuleViolationException>(() => Booking.Receive(
            code, "s", BookingType.Api, BookingStatus.Cancelled, 10m, SyncDirection.Inbound, At));
    }

    [Fact]
    public void Cancel_sets_status_and_raises_once()
    {
        var booking = Receive();
        booking.DequeueDomainEvents();

        booking.Cancel(At.AddHours(1));

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(At.AddHours(1), booking.CancelledAt);
        var evt = Assert.IsType<BookingCancelled>(Assert.Single(booking.DomainEvents));
        Assert.Equal("MT-7KQ2XY9Z", evt.Code);
        Assert.Throws<BookingRuleViolationException>(() => booking.Cancel(At.AddHours(2)));
    }

    [Fact]
    public void AddItem_enforces_invariants_and_exposes_read_only_children()
    {
        var booking = Receive();

        booking.AddItem(501, 42, 7, "adult", At.AddDays(3), 120m);

        Assert.Single(booking.Items);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AddItem(501, 42, 7, "child", At.AddDays(3), 60m));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AddItem(502, 42, 7, "adult", At.AddDays(3), -5m));
    }

    [Fact]
    public void CheckInItem_flips_the_flag()
    {
        var booking = Receive();
        booking.AddItem(501, 42, 7, "adult", At.AddDays(3), 120m);

        booking.CheckInItem(501);

        Assert.True(booking.Items.Single().CheckedIn);
        Assert.Throws<BookingRuleViolationException>(() => booking.CheckInItem(999));
    }

    [Fact]
    public void AddExtra_enforces_positive_quantity()
    {
        var booking = Receive();

        booking.AddExtra(801, 7, "lunch", "Picnic lunch", At.AddDays(3), 2, 30m);

        Assert.Single(booking.Extras);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AddExtra(802, 7, "lunch", null, null, 0, 30m));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AddExtra(801, 7, "lunch", null, null, 1, 30m));
    }

    [Fact]
    public void RecordPayment_and_ApplyGiftCard_enforce_amounts()
    {
        var booking = Receive();

        booking.RecordPayment(9001, 180.50m, "card", PaymentStatus.Captured, At);
        booking.ApplyGiftCard("MTGIFT-01", 25m);

        Assert.Single(booking.Payments);
        Assert.Single(booking.GiftCards);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.RecordPayment(9002, -1m, "card", PaymentStatus.Pending, null));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.RecordPayment(9001, 10m, "card", PaymentStatus.Pending, null));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.ApplyGiftCard("MTGIFT-01", 25m));
    }

    [Fact]
    public void Sync_transitions_are_aggregate_behavior()
    {
        var booking = Receive();

        booking.ClaimForSync();
        Assert.Equal(SyncStatus.Processing, booking.Sync.Status);

        booking.MarkSynced(At);
        Assert.Equal(SyncStatus.Synced, booking.Sync.Status);
        Assert.Equal(At, booking.Sync.SyncedAt);

        Assert.Throws<InvalidSyncTransitionException>(() => booking.ClaimForSync());
    }

    [Fact]
    public void MarkSyncFailed_records_the_error_and_raises()
    {
        var booking = Receive();
        booking.DequeueDomainEvents();
        booking.ClaimForSync();

        booking.MarkSyncFailed("provider unreachable", At);

        Assert.Equal(SyncStatus.Failed, booking.Sync.Status);
        Assert.Equal("provider unreachable", booking.Sync.Error);
        var evt = Assert.IsType<BookingSyncFailed>(Assert.Single(booking.DomainEvents));
        Assert.Equal("provider unreachable", evt.Error);

        booking.RequeueSync();
        Assert.Equal(SyncStatus.Pending, booking.Sync.Status);
        Assert.Null(booking.Sync.Error);
    }

    [Fact]
    public void Details_setters_update_the_read_model()
    {
        var booking = Receive();

        booking.UpdateContact("Alex Rivera", "alex@example.com", "+1-555-0100");
        booking.SetChannelReference("CH-REF-1", "partner-site");
        booking.SetActivityLanguage("en");
        booking.SetExternalTimestamps(At.AddDays(-1), At);

        Assert.Equal("Alex Rivera", booking.LeadContactName);
        Assert.Equal("alex@example.com", booking.Email);
        Assert.Equal("+1-555-0100", booking.Phone);
        Assert.Equal("CH-REF-1", booking.ChannelBookingCode);
        Assert.Equal("partner-site", booking.Referrer);
        Assert.Equal("en", booking.ActivityLanguage);
        Assert.Equal(At.AddDays(-1), booking.CreatedAtExternal);
        Assert.Equal(At, booking.UpdatedAtExternal);
    }
}
