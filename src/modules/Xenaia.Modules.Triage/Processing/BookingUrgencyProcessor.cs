using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Core.BusinessHours;
using Xenaia.Core.Notifications;
using Xenaia.Core.Tenancy;
using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Modules.Triage.Processing;

/// <summary>
/// Escalates a matched booking ticket to urgent when the extracted booking
/// start (captures bookingDateTime, plus time when present) is imminent and
/// falls inside business hours. Anything unparseable is logged and skipped:
/// urgency is best effort, categorization already succeeded.
/// </summary>
public sealed class BookingUrgencyProcessor(
    IOptions<TriageOptions> triageOptions,
    IOptions<TenantProfileOptions> tenantOptions,
    IBusinessHoursService businessHours,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<BookingUrgencyProcessor> logger) : ITicketProcessor
{
    public const string ProcessorName = "booking-urgency";

    public string Name => ProcessorName;

    public async Task ProcessAsync(TriageContext context, CancellationToken ct)
    {
        if (!context.Captures.TryGetValue("bookingDateTime", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning(
                "Ticket {TicketId}: no bookingDateTime capture; skipping urgency check",
                context.Ticket.Id);
            return;
        }
        if (context.Captures.TryGetValue("time", out var time) && !string.IsNullOrWhiteSpace(time))
            raw = $"{raw} {time}";

        var bookingStart = ParseInTenantZone(raw);
        if (bookingStart is null)
        {
            logger.LogWarning(
                "Ticket {TicketId}: booking start '{Raw}' matched no configured format; skipping urgency check",
                context.Ticket.Id, raw);
            return;
        }

        var untilStart = bookingStart.Value - clock.GetUtcNow();
        if (untilStart < TimeSpan.Zero
            || untilStart > TimeSpan.FromHours(triageOptions.Value.Urgency.ProximityHours))
            return;
        if (!businessHours.IsOpenAt(bookingStart.Value))
            return;

        context.Draft.Status = TicketStatus.Open;
        context.Draft.Priority = TicketPriority.Urgent;

        var metadata = new Dictionary<string, string>
        {
            ["ticketId"] = context.Ticket.Id,
            ["category"] = context.Category,
            ["bookingStartUtc"] = bookingStart.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };
        if (context.Captures.TryGetValue("bookingCode", out var bookingCode))
            metadata["bookingCode"] = bookingCode;

        logger.LogInformation(
            "Ticket {TicketId}: booking starts {BookingStart}; escalated to urgent",
            context.Ticket.Id, bookingStart.Value);

        await notifications.SendAsync(new Notification(
            $"Urgent booking ticket {context.Ticket.Id}",
            $"Booking starts at {bookingStart.Value:yyyy-MM-dd HH:mm zzz}; the ticket was escalated to urgent.",
            NotificationSeverity.Warning,
            metadata), ct);
    }

    private DateTimeOffset? ParseInTenantZone(string raw)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tenantOptions.Value.TimeZone);
        foreach (var format in triageOptions.Value.Urgency.DateTimeFormats)
        {
            if (!DateTime.TryParseExact(
                    raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                continue;
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        return null;
    }
}
