using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.Notifications;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Bookings;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;
using Xenaia.PortContracts.Fakes;

namespace Xenaia.Modules.Sync.Tests.Bookings;

public class BookingPusherTests
{
    private const int ProductId = 100;
    private const int OptionId = 7;
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ActivityAt = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly CodeFormats _formats = new(Options.Create(new BookingsFormatOptions
    {
        BookingCodePattern = "^MT-[0-9]{4,}$",
        ProductCodePattern = "^MTP-[A-Z0-9]{4}$",
    }));

    [Fact]
    public async Task Push_create_calls_the_vendor_ingests_the_confirmed_snapshot_outbound_and_marks_synced()
    {
        var store = new FakeOutboundBookingRequestStore();
        var request = store.Seed(OutboundBookingRequest.ForCreate(SerializeDraft(DraftFor(ProductId, OptionId))));
        var provider = new InMemoryBookingSystemProvider();
        var bookingStore = new FakeBookingStore();
        var sut = CreateSut(store, provider, bookingStore);

        var outcome = await sut.ProcessAsync(request.Id, CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, outcome);
        Assert.Equal(SyncStatus.Synced, request.Sync.Status);

        var received = Assert.Single(provider.ReceivedCreates);
        Assert.Equal(ProductId, received.Items.Single().ProductExternalId);

        var booking = Assert.Single(bookingStore.Bookings);
        Assert.Equal("MT-1000", booking.Code.Value);            // InMemory provider's first assigned code
        Assert.Equal(SyncDirection.Outbound, booking.Direction);
        Assert.Equal(SyncStatus.Synced, booking.Sync.Status);
    }

    [Fact]
    public async Task Push_cancel_calls_the_vendor_cancels_the_local_aggregate_and_marks_synced()
    {
        var store = new FakeOutboundBookingRequestStore();
        var request = store.Seed(OutboundBookingRequest.ForCancel("MT-2000"));
        var provider = new InMemoryBookingSystemProvider();
        provider.SeedBooking(new BookingSnapshot { Code = "MT-2000", Status = BookingStatus.Pending });
        var bookingStore = new FakeBookingStore();
        var booking = ActiveBooking("MT-2000");
        bookingStore.Seed(booking);
        var sut = CreateSut(store, provider, bookingStore);

        var outcome = await sut.ProcessAsync(request.Id, CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, outcome);
        Assert.Equal(SyncStatus.Synced, request.Sync.Status);
        Assert.Contains("MT-2000", provider.ReceivedCancels);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(FixedNow, booking.CancelledAt);
        Assert.True(bookingStore.SaveChangesCount >= 1);
    }

    [Fact]
    public async Task Vendor_not_found_on_cancel_fails_permanently_with_one_call_and_a_notification()
    {
        var store = new FakeOutboundBookingRequestStore();
        var request = store.Seed(OutboundBookingRequest.ForCancel("MT-3000"));
        var provider = new FailingBookingSystemProvider(
            new BookingSystemEntityNotFoundException("Unknown booking code 'MT-3000'."));
        var notifications = new FakeNotificationService();
        var sut = CreateSut(store, provider, new FakeBookingStore(), notifications);

        var outcome = await sut.ProcessAsync(request.Id, CancellationToken.None);

        Assert.Equal(PushOutcome.Failed, outcome);
        Assert.Equal(SyncStatus.Failed, request.Sync.Status);
        Assert.Equal(1, provider.CancelCallCount);              // permanent: never retried
        Assert.Single(notifications.Sent);
    }

    [Fact]
    public async Task Retryable_failure_exhausted_fails_truncates_the_error_and_notifies_once_as_warning()
    {
        var store = new FakeOutboundBookingRequestStore();
        var request = store.Seed(OutboundBookingRequest.ForCreate(SerializeDraft(DraftFor(ProductId, OptionId))));
        var longMessage = new string('x', 2500);
        var provider = new FailingBookingSystemProvider(new BookingSystemException(longMessage));
        var notifications = new FakeNotificationService();
        var delays = new List<TimeSpan>();
        var sut = CreateSut(
            store, provider, new FakeBookingStore(), notifications,
            delayer: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        var outcome = await sut.ProcessAsync(request.Id, CancellationToken.None);

        Assert.Equal(PushOutcome.Failed, outcome);
        Assert.Equal(SyncStatus.Failed, request.Sync.Status);
        Assert.Equal(4, provider.CreateCallCount);              // retried to exhaustion
        Assert.Equal(3, delays.Count);                          // 3 backoffs between 4 tries
        Assert.Equal(2000, request.Sync.Error!.Length);
        Assert.Equal(new string('x', 2000), request.Sync.Error);

        var notification = Assert.Single(notifications.Sent);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.NotNull(notification.Metadata);
        Assert.Equal(request.Id.ToString(), notification.Metadata!["requestId"]);
    }

    [Fact]
    public async Task Recovery_resets_processing_and_re_enqueues_all_pending()
    {
        var store = new FakeOutboundBookingRequestStore();
        var stuck = store.Seed(OutboundBookingRequest.ForCreate(SerializeDraft(DraftFor(ProductId, OptionId))));
        stuck.ClaimForSync();                                   // Processing (stuck)
        var pending = store.Seed(OutboundBookingRequest.ForCancel("MT-4000")); // already Pending
        var channel = new BookingChannel(100);
        var sut = CreateSut(store, new InMemoryBookingSystemProvider(), new FakeBookingStore(), channel: channel);

        var enqueued = await sut.RecoverAsync(CancellationToken.None);

        Assert.Equal(2, enqueued);
        Assert.Equal(SyncStatus.Pending, stuck.Sync.Status);
        Assert.Equal(SyncStatus.Pending, pending.Sync.Status);

        var ids = Drain(channel);
        Assert.Equal(new[] { stuck.Id, pending.Id }.ToHashSet(), ids.ToHashSet());
    }

    [Fact]
    public async Task Lost_claim_returns_lost_claim_and_never_calls_the_vendor()
    {
        var store = new FakeOutboundBookingRequestStore();
        var request = store.Seed(OutboundBookingRequest.ForCreate(SerializeDraft(DraftFor(ProductId, OptionId))));
        request.ClaimForSync();                                 // already Processing: the claim will fail
        var provider = new InMemoryBookingSystemProvider();
        var sut = CreateSut(store, provider, new FakeBookingStore());

        var outcome = await sut.ProcessAsync(request.Id, CancellationToken.None);

        Assert.Equal(PushOutcome.LostClaim, outcome);
        Assert.Empty(provider.ReceivedCreates);
        Assert.Equal(SyncStatus.Processing, request.Sync.Status); // untouched
    }

    private static string SerializeDraft(BookingDraft draft) => JsonSerializer.Serialize(draft, Web);

    private static BookingDraft DraftFor(int productExternalId, int optionExternalId) => new()
    {
        Type = BookingType.Api,
        LeadContactName = "Ada Coastline",
        Items = [new BookingDraftItem(productExternalId, optionExternalId, "adult", ActivityAt, 49.50m)],
    };

    private Booking ActiveBooking(string code) => Booking.Receive(
        BookingCode.Create(code, _formats.BookingCode), $"SEC-{code}",
        BookingType.Api, BookingStatus.Pending, 49.50m, SyncDirection.Outbound, ActivityAt);

    private BookingPusher CreateSut(
        FakeOutboundBookingRequestStore store,
        IBookingSystemProvider provider,
        FakeBookingStore bookingStore,
        INotificationService? notifications = null,
        BookingChannel? channel = null,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        var clock = new FakeTimeProvider(FixedNow);
        var ingest = new BookingIngestService(bookingStore, _formats, clock);
        return new BookingPusher(
            store, provider, ingest, bookingStore, channel ?? new BookingChannel(100),
            notifications ?? new FakeNotificationService(), Options.Create(new SyncOptions()),
            clock, NullLogger<BookingPusher>.Instance,
            delayer ?? ((_, _) => Task.CompletedTask));
    }

    private static List<int> Drain(BookingChannel channel)
    {
        var ids = new List<int>();
        while (channel.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }
}
