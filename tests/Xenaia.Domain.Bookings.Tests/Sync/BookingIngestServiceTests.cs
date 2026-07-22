using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Domain.Bookings.Tests.Fakes;

namespace Xenaia.Domain.Bookings.Tests.Sync;

public class BookingIngestServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly CodeFormat Format = CodeFormat.Create("^MT-[0-9]{4,}$");

    private readonly FakeBookingStore _store = new();
    private readonly FakeTimeProvider _clock = new(At);

    private readonly CodeFormats _formats = new(Options.Create(new BookingsFormatOptions
    {
        BookingCodePattern = "^MT-[0-9]{4,}$",
        ProductCodePattern = "^MTP-[A-Z0-9]{4}$",
    }));

    private BookingIngestService CreateSut() => new(_store, _formats, _clock);

    private static DateTimeOffset Date(int daysAhead) => At.AddDays(daysAhead);

    private static BookingSnapshot Snapshot(string code) => new()
    {
        Code = code,
        SecretCode = "secret-1",
        Type = BookingType.Api,
        Status = BookingStatus.Pending,
        FinalPrice = 100m,
        LeadContactName = "Alex Doe",
        Email = "alex.doe@example.com",
        Phone = "+1-555-0100",
        ActivityLanguage = "en",
        ChannelBookingCode = "CHN-1",
        Referrer = "web",
        CreatedAtExternal = At,
        UpdatedAtExternal = At,
    };

    // Helper: Booking.Receive + AddItem(1, ...) + AddItem(2, ...), synced.
    private static Booking ReceiveBooking(string code)
    {
        var booking = Booking.Receive(
            BookingCode.Create(code, Format),
            secretCode: "secret-0",
            type: BookingType.Api,
            status: BookingStatus.Pending,
            finalPrice: 80m,
            direction: SyncDirection.Inbound,
            occurredAt: At);
        booking.AddItem(1, 10, 20, "adult", Date(3), 80m);
        booking.AddItem(2, 10, 20, "adult", Date(3), 80m);
        booking.ClaimForSync();
        booking.MarkSynced(At);
        return booking;
    }

    [Fact]
    public async Task New_snapshot_is_born_with_direction_scalars_and_children_synced()
    {
        var sut = CreateSut();
        var snapshot = Snapshot("MT-1001") with
        {
            Items = [new(1, 10, 20, "adult", Date(3), 100m)],
            Extras = [new(1, 20, "lunch", "Picnic lunch", Date(3), 2, 30m)],
            Payments = [new(1, 100m, "card", PaymentStatus.Captured, At)],
            GiftCards = [new("MTGIFT-1", 10m)],
        };

        var booking = await sut.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, default);

        Assert.Equal(SyncDirection.Inbound, booking.Direction);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(100m, booking.FinalPrice);
        Assert.Equal("Alex Doe", booking.LeadContactName);
        Assert.Equal("alex.doe@example.com", booking.Email);
        Assert.Equal("CHN-1", booking.ChannelBookingCode);
        Assert.Equal("web", booking.Referrer);
        Assert.Equal("en", booking.ActivityLanguage);
        Assert.Equal(At, booking.CreatedAtExternal);
        Assert.Single(booking.Items);
        Assert.Single(booking.Extras);
        Assert.Single(booking.Payments);
        Assert.Single(booking.GiftCards);
        Assert.Equal(SyncStatus.Synced, booking.Sync.Status);
        Assert.Equal(1, _store.SaveChangesCount);
    }

    [Fact]
    public async Task New_snapshot_already_cancelled_is_received_pending_then_cancelled()
    {
        var sut = CreateSut();
        var snapshot = Snapshot("MT-1002") with
        {
            Status = BookingStatus.Cancelled,
            CancelledAt = At,
        };

        var booking = await sut.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, default);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(At, booking.CancelledAt);
        Assert.Equal(SyncStatus.Synced, booking.Sync.Status);
    }

    [Fact]
    public async Task Existing_booking_scalar_drift_is_applied_via_apply_status_and_reprice()
    {
        var existing = ReceiveBooking("MT-1003");
        _store.Seed(existing);
        var sut = CreateSut();
        var snapshot = Snapshot("MT-1003") with
        {
            Status = BookingStatus.Completed,
            FinalPrice = 150m,
            Items =
            [
                new(1, 10, 20, "adult", Date(3), 80m),
                new(2, 10, 20, "adult", Date(3), 80m),
            ],
        };

        var booking = await sut.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, default);

        Assert.Same(existing, booking);
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal(150m, booking.FinalPrice);
        Assert.Equal(SyncStatus.Synced, booking.Sync.Status);
        Assert.Equal(1, _store.SaveChangesCount);
    }

    [Fact]
    public async Task Child_add_amend_remove_in_one_snapshot_lands_all_three()
    {
        var existing = ReceiveBooking("MT-1004");
        _store.Seed(existing);
        var sut = CreateSut();
        var snapshot = Snapshot("MT-1004") with
        {
            Items =
            [
                new(1, 10, 20, "adult", Date(3), 100m),     // amend: price was 80m
                new(3, 10, 20, "child", Date(3), 50m),      // add
            ],                                               // external id 2: removed
        };

        var booking = await sut.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, default);

        Assert.Equal(2, booking.Items.Count);
        var amended = Assert.Single(booking.Items, i => i.ExternalId == 1);
        Assert.Equal(100m, amended.FinalPrice);
        Assert.Equal("adult", amended.ParticipantTypeAlias);
        var added = Assert.Single(booking.Items, i => i.ExternalId == 3);
        Assert.Equal(50m, added.FinalPrice);
        Assert.Equal("child", added.ParticipantTypeAlias);
        Assert.DoesNotContain(booking.Items, i => i.ExternalId == 2);
    }

    [Fact]
    public async Task Cancel_transition_cancels_a_local_active_booking()
    {
        var existing = ReceiveBooking("MT-1005");
        _store.Seed(existing);
        var sut = CreateSut();
        var snapshot = Snapshot("MT-1005") with
        {
            Status = BookingStatus.Cancelled,
            CancelledAt = At,
            Items =
            [
                new(1, 10, 20, "adult", Date(3), 80m),
                new(2, 10, 20, "adult", Date(3), 80m),
            ],
        };

        var booking = await sut.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, default);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(At, booking.CancelledAt);
        Assert.Equal(SyncStatus.Synced, booking.Sync.Status);
    }

    [Fact]
    public async Task Malformed_code_propagates_invalid_code_exception()
    {
        var sut = CreateSut();
        var snapshot = Snapshot("BAD-CODE");

        await Assert.ThrowsAsync<InvalidCodeException>(
            () => sut.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, default));
    }
}
