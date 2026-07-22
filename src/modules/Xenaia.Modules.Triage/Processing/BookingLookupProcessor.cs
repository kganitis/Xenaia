using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Modules.Triage.Processing;

/// <summary>
/// The first booking-aware ticket processor: resolves the ticket's
/// bookingCode capture against the local Booking store, falling back to the
/// booking system when the local copy has not seen it yet, and leaves a
/// private note summarizing the outcome. Purely informational: no tags, no
/// status changes, declarative actions on the matching rule keep owning
/// those. Registers only when the host has a booking system configured
/// (TriageServiceCollectionExtensions.AddTriageModule); a rule pack naming
/// it without one fails startup via TriageOptionsValidator.
/// </summary>
public sealed class BookingLookupProcessor(
    IBookingStore store,
    IBookingSystemProvider provider,
    BookingIngestService ingest,
    CodeFormats codeFormats,
    ILogger<BookingLookupProcessor> logger) : ITicketProcessor
{
    public const string ProcessorName = "booking-lookup";

    public string Name => ProcessorName;

    public async Task ProcessAsync(TriageContext context, CancellationToken ct)
    {
        if (!context.Captures.TryGetValue("bookingCode", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning(
                "Ticket {TicketId}: no bookingCode capture; skipping booking lookup", context.Ticket.Id);
            return;
        }

        BookingCode code;
        try
        {
            code = BookingCode.Create(raw, codeFormats.BookingCode);
        }
        catch (InvalidCodeException)
        {
            logger.LogWarning(
                "Ticket {TicketId}: booking code '{Code}' does not match the tenant format",
                context.Ticket.Id, raw);
            context.Draft.AddNote(
                $"Booking lookup: the code '{WebUtility.HtmlEncode(raw)}' looks invalid and was not looked up.");
            return;
        }

        try
        {
            var booking = await store.GetByCodeAsync(code.Value, ct);
            if (booking is null)
            {
                var snapshot = await provider.GetBookingByCodeAsync(code.Value, ct);
                if (snapshot is null)
                {
                    logger.LogInformation(
                        "Ticket {TicketId}: no booking found for code {Code}", context.Ticket.Id, code.Value);
                    context.Draft.AddNote(
                        $"Booking lookup: no booking found for code {WebUtility.HtmlEncode(code.Value)}.");
                    return;
                }

                booking = await ingest.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, ct);
            }

            context.Draft.AddNote(Summarize(booking));
        }
        catch (BookingSystemException ex)
        {
            logger.LogError(
                ex, "Ticket {TicketId}: booking lookup unavailable for code {Code}",
                context.Ticket.Id, code.Value);
            context.Draft.AddNote(
                $"Booking lookup unavailable for code {WebUtility.HtmlEncode(code.Value)}: " +
                "the booking system could not be reached.");
        }
    }

    private static string Summarize(Booking booking)
    {
        var paidTotal = booking.Payments
            .Where(p => p.Status == PaymentStatus.Captured)
            .Sum(p => p.Amount);
        var activityDates = DescribeActivityDates(booking.Items);

        return "Booking lookup for " + WebUtility.HtmlEncode(booking.Code.Value) + ": status " +
            WebUtility.HtmlEncode(booking.Status.ToString()) + ", lead contact " +
            WebUtility.HtmlEncode(booking.LeadContactName ?? "unknown") + ", activity " +
            WebUtility.HtmlEncode(activityDates) + ", " + booking.Items.Count +
            " item(s), paid total " + paidTotal.ToString("F2", CultureInfo.InvariantCulture) + ".";
    }

    private static string DescribeActivityDates(IReadOnlyCollection<BookingItem> items)
    {
        if (items.Count == 0)
            return "no scheduled activity";

        var first = items.Min(i => i.ActivityAt);
        var last = items.Max(i => i.ActivityAt);
        return first == last
            ? first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : $"{first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to " +
              last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
