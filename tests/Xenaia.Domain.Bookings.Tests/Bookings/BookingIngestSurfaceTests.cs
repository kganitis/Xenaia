using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Bookings;

public class BookingIngestSurfaceTests
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
    public void ApplyStatus_changes_status_but_rejects_cancelled()
    {
        var booking = Receive();

        booking.ApplyStatus(BookingStatus.Completed);

        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.ApplyStatus(BookingStatus.Cancelled));
    }

    [Fact]
    public void Reprice_changes_price_and_rejects_negative()
    {
        var booking = Receive();

        booking.Reprice(200m);

        Assert.Equal(200m, booking.FinalPrice);
        Assert.Throws<BookingRuleViolationException>(() => booking.Reprice(-1m));
    }

    [Fact]
    public void AmendItem_changes_fields_and_rejects_unknown_id()
    {
        var booking = Receive();
        booking.AddItem(501, 42, 7, "adult", At.AddDays(3), 120m);

        booking.AmendItem(501, "child", At.AddDays(4), 90m);

        var item = Assert.Single(booking.Items);
        Assert.Equal("child", item.ParticipantTypeAlias);
        Assert.Equal(At.AddDays(4), item.ActivityAt);
        Assert.Equal(90m, item.FinalPrice);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendItem(999, "adult", At, 100m));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendItem(501, "adult", At, -1m));
    }

    [Fact]
    public void RemoveItem_removes_known_and_ignores_unknown_id()
    {
        var booking = Receive();
        booking.AddItem(501, 42, 7, "adult", At.AddDays(3), 120m);

        booking.RemoveItem(999);
        Assert.Single(booking.Items);

        booking.RemoveItem(501);
        Assert.Empty(booking.Items);
    }

    [Fact]
    public void AmendExtra_changes_fields_and_rejects_unknown_id()
    {
        var booking = Receive();
        booking.AddExtra(801, 7, "lunch", "Picnic lunch", At.AddDays(3), 2, 30m);

        booking.AmendExtra(801, "Deluxe picnic", At.AddDays(4), 3, 45m);

        var extra = Assert.Single(booking.Extras);
        Assert.Equal("Deluxe picnic", extra.Title);
        Assert.Equal(At.AddDays(4), extra.ActivityAt);
        Assert.Equal(3, extra.Quantity);
        Assert.Equal(45m, extra.FinalPrice);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendExtra(999, "x", null, 1, 10m));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendExtra(801, "x", null, 0, 10m));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendExtra(801, "x", null, 1, -1m));
    }

    [Fact]
    public void RemoveExtra_removes_known_and_ignores_unknown_id()
    {
        var booking = Receive();
        booking.AddExtra(801, 7, "lunch", "Picnic lunch", At.AddDays(3), 2, 30m);

        booking.RemoveExtra(999);
        Assert.Single(booking.Extras);

        booking.RemoveExtra(801);
        Assert.Empty(booking.Extras);
    }

    [Fact]
    public void AmendPayment_changes_fields_and_rejects_unknown_id()
    {
        var booking = Receive();
        booking.RecordPayment(9001, 180.50m, "card", PaymentStatus.Pending, null);

        booking.AmendPayment(9001, 200m, "bank_transfer", PaymentStatus.Captured, At);

        var payment = Assert.Single(booking.Payments);
        Assert.Equal(200m, payment.Amount);
        Assert.Equal("bank_transfer", payment.PaymentMethod);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(At, payment.PaidAt);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendPayment(999, 1m, "card", PaymentStatus.Pending, null));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendPayment(9001, -1m, "card", PaymentStatus.Pending, null));
    }

    [Fact]
    public void RemovePayment_removes_known_and_ignores_unknown_id()
    {
        var booking = Receive();
        booking.RecordPayment(9001, 180.50m, "card", PaymentStatus.Pending, null);

        booking.RemovePayment(999);
        Assert.Single(booking.Payments);

        booking.RemovePayment(9001);
        Assert.Empty(booking.Payments);
    }

    [Fact]
    public void AmendGiftCard_changes_amount_and_rejects_unknown_code()
    {
        var booking = Receive();
        booking.ApplyGiftCard("MTGIFT-01", 25m);

        booking.AmendGiftCard("MTGIFT-01", 40m);

        var giftCard = Assert.Single(booking.GiftCards);
        Assert.Equal(40m, giftCard.Amount);
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendGiftCard("MTGIFT-99", 10m));
        Assert.Throws<BookingRuleViolationException>(
            () => booking.AmendGiftCard("MTGIFT-01", -1m));
    }

    [Fact]
    public void RemoveGiftCard_removes_known_and_ignores_unknown_code()
    {
        var booking = Receive();
        booking.ApplyGiftCard("MTGIFT-01", 25m);

        booking.RemoveGiftCard("MTGIFT-99");
        Assert.Single(booking.GiftCards);

        booking.RemoveGiftCard("MTGIFT-01");
        Assert.Empty(booking.GiftCards);
    }
}
