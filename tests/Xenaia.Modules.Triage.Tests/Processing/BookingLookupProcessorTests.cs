using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xenaia.PortContracts.BookingSystem;
using Xenaia.PortContracts.Fakes;
using Xunit;

namespace Xenaia.Modules.Triage.Tests.Processing;

public class BookingLookupProcessorTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly CodeFormat Format = CodeFormat.Create("^MT-[A-Z0-9]{8}$");

    private readonly FakeBookingStore _store = new();
    private readonly InMemoryBookingSystemProvider _provider = new();
    private readonly CodeFormats _codeFormats = new(Options.Create(new BookingsFormatOptions
    {
        BookingCodePattern = "^MT-[A-Z0-9]{8}$",
        ProductCodePattern = "^MTP-[A-Z0-9]{4}$",
    }));

    private BookingLookupProcessor Build() => new(
        _store, _provider,
        new BookingIngestService(_store, _codeFormats, new FakeTimeProvider(At)),
        _codeFormats,
        NullLogger<BookingLookupProcessor>.Instance);

    private static TriageContext Context(params (string Key, string Value)[] captures) => new(
        new HelpdeskTicket { Id = "9" },
        "booking-status-inquiry",
        captures.ToDictionary(c => c.Key, c => c.Value),
        new TicketUpdateDraft());

    private static Booking Seed(FakeBookingStore store, string code)
    {
        var booking = Booking.Receive(
            BookingCode.Create(code, Format),
            secretCode: "secret-0",
            type: BookingType.Api,
            status: BookingStatus.Completed,
            finalPrice: 150m,
            direction: SyncDirection.Inbound,
            occurredAt: At);
        booking.UpdateContact("Alex Doe", "alex.doe@example.com", "+1-555-0100");
        booking.AddItem(1, 10, 20, "adult", At.AddDays(3), 150m);
        booking.RecordPayment(1, 150m, "card", PaymentStatus.Captured, At);
        booking.ClaimForSync();
        booking.MarkSynced(At);
        store.Seed(booking);
        return booking;
    }

    [Fact]
    public async Task No_capture_leaves_the_draft_untouched()
    {
        var processor = Build();
        var context = Context();

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Empty(context.Draft.Notes);
        Assert.Null(context.Draft.Status);
        Assert.Null(context.Draft.Priority);
        Assert.Empty(context.Draft.Tags);
    }

    [Fact]
    public async Task Code_failing_the_tenant_format_notes_the_offending_code_without_calling_the_provider()
    {
        var processor = Build();
        _provider.FailNextCallWith = new InvalidOperationException("provider should not be called");
        var context = Context(("bookingCode", "NOT-A-CODE"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.Contains("NOT-A-CODE", note);
        Assert.Contains("invalid", note);
        Assert.Null(context.Draft.Status);
        Assert.Empty(context.Draft.Tags);
    }

    [Fact]
    public async Task Db_hit_summarizes_the_booking_in_a_private_note()
    {
        var processor = Build();
        Seed(_store, "MT-1A2B3C4D");
        var context = Context(("bookingCode", "MT-1A2B3C4D"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.Contains("MT-1A2B3C4D", note);
        Assert.Contains("Completed", note);
        Assert.Contains("Alex Doe", note);
        Assert.Contains("2026-08-15", note);
        Assert.Contains("1 item(s)", note);
        Assert.Contains("150.00", note);
        Assert.Null(context.Draft.Status);
        Assert.Empty(context.Draft.Tags);
    }

    [Fact]
    public async Task Db_miss_falls_back_to_the_provider_and_ingests_so_the_next_lookup_hits_the_db()
    {
        var processor = Build();
        _provider.SeedBooking(new BookingSnapshot
        {
            Code = "MT-5E6F7G8H",
            SecretCode = "SEC-1",
            Type = BookingType.Api,
            Status = BookingStatus.Completed,
            FinalPrice = 90m,
            LeadContactName = "Jamie Rivers",
            CreatedAtExternal = At,
            UpdatedAtExternal = At,
            Items = [new BookingItemSnapshot(1, 10, 20, "adult", At.AddDays(5), 90m)],
            Payments = [new BookingPaymentSnapshot(1, 90m, "card", PaymentStatus.Captured, At)],
        });
        var context = Context(("bookingCode", "MT-5E6F7G8H"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.Contains("MT-5E6F7G8H", note);
        Assert.Contains("Jamie Rivers", note);

        var ingested = await _store.GetByCodeAsync("MT-5E6F7G8H", CancellationToken.None);
        Assert.NotNull(ingested);
    }

    [Fact]
    public async Task Not_found_anywhere_notes_that_no_booking_was_found()
    {
        var processor = Build();
        var context = Context(("bookingCode", "MT-9Z8Y7X6W"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.Contains("no booking found", note);
        Assert.Contains("MT-9Z8Y7X6W", note);
    }

    [Fact]
    public async Task Provider_exception_is_caught_and_noted_never_failing_the_sweep()
    {
        var processor = Build();
        _provider.FailNextCallWith = new BookingSystemException("booking system unreachable");
        var context = Context(("bookingCode", "MT-2K3L4M5N"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.Contains("unavailable", note);
        Assert.Contains("MT-2K3L4M5N", note);
    }

    [Fact]
    public async Task Hostile_lead_contact_name_is_html_encoded_in_the_note()
    {
        var processor = Build();
        var booking = Seed(_store, "MT-3P4Q5R6S");
        booking.UpdateContact("<script>alert('x')</script>", null, null);
        var context = Context(("bookingCode", "MT-3P4Q5R6S"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.DoesNotContain("<script>", note);
        Assert.Contains("&lt;script&gt;", note);
    }

    [Fact]
    public async Task Hostile_booking_code_capture_is_html_encoded_when_the_format_rejects_it()
    {
        var processor = Build();
        var context = Context(("bookingCode", "<b>MT-BAD</b>"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var note = Assert.Single(context.Draft.Notes);
        Assert.DoesNotContain("<b>", note);
        Assert.Contains("&lt;b&gt;", note);
    }
}
