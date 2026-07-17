using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.BusinessHours;
using Xenaia.Core.Notifications;
using Xenaia.Core.Tenancy;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;
using Xenaia.PortContracts.Helpdesk;
using Xunit;

namespace Xenaia.Modules.Triage.Tests;

/// <summary>
/// Milestone 1: poll, categorize, process, close, end to end on the shipped
/// Meridian Trails sample pack with the real engine, real actions, and the
/// real urgency processor. Only the helpdesk and the clock are test doubles.
/// </summary>
public class MilestoneOneEndToEndTests
{
    private sealed class CollectingNotifications : INotificationService
    {
        public List<Notification> Sent { get; } = [];
        public Task SendAsync(Notification notification, CancellationToken ct = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysOpen : IBusinessHoursService
    {
        public bool IsOpenNow() => true;
        public bool IsOpenAt(DateTimeOffset instant) => true;
        public DateTimeOffset? NextOpeningAfter(DateTimeOffset instant) => null;
    }

    private sealed class FixedRulePackProvider(RulePack pack) : IRulePackProvider
    {
        public RulePack Pack { get; } = pack;
    }

    // Wednesday 2026-08-12, 10:00 in America/New_York.
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 14, 0, 0, TimeSpan.Zero);

    private static HelpdeskTicket Ticket(string id, string subject, string bodyHtml) => new()
    {
        Id = id,
        Subject = subject,
        BodyHtml = bodyHtml,
        Status = TicketStatus.Open,
        Priority = TicketPriority.Low,
        CreatedAt = Now.AddMinutes(-30),
    };

    [Fact]
    public async Task One_sweep_closes_escalates_and_flags_for_humans()
    {
        var pack = RulePackLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "meridian-trails.yaml"));
        var provider = new InMemoryHelpdeskProvider(
        [
            // Booking starting 12:30 local (2.5 h away): escalates.
            Ticket("1", "New Booking [MT-7Q2K9F4A]",
                "<table><tr><th>Product Code</th><td>MTP-KAYA</td></tr>" +
                "<tr><th>Date</th><td>12/08/2026</td><th>Time</th><td>12:30</td></tr></table>"),
            // Review notification: closes.
            Ticket("2", "You have a new review on TrailBooker",
                "<p>Five stars for the Sunset Kayak Tour.</p>"),
            // Nothing matches: needs-human, untouched beyond the marker.
            Ticket("3", "Do you allow dogs on kayaks?",
                "<p>Visiting in August with our dog.</p>"),
        ]);
        var notifications = new CollectingNotifications();
        var triageOptions = Options.Create(new TriageOptions { RulePackPath = "unused; injected" });
        var urgency = new BookingUrgencyProcessor(
            triageOptions,
            Options.Create(new TenantProfileOptions { TimeZone = "America/New_York" }),
            new AlwaysOpen(),
            notifications,
            new FakeTimeProvider(Now),
            NullLogger<BookingUrgencyProcessor>.Instance);
        var sweep = new TriageSweep(
            provider,
            new FixedRulePackProvider(pack),
            new RuleEvaluator(NullLogger<RuleEvaluator>.Instance),
            [urgency],
            notifications,
            NullLogger<TriageSweep>.Instance);

        await sweep.RunAsync(CancellationToken.None);

        // Ticket 1: escalated, stamped, booking code in the custom field.
        var booking = provider.Ticket("1");
        Assert.Equal(TicketPriority.Urgent, booking.Priority);
        Assert.Equal(TicketStatus.Open, booking.Status);
        Assert.Contains(TriageConstants.MarkerTag, booking.Tags);
        Assert.Contains("auto-triaged", booking.Tags);
        Assert.Equal("MT-7Q2K9F4A", booking.CustomFields["bookingCode"]);

        // Ticket 2: closed and tagged.
        var review = provider.Ticket("2");
        Assert.Equal(TicketStatus.Closed, review.Status);
        Assert.Contains("review", review.Tags);
        Assert.Contains(TriageConstants.MarkerTag, review.Tags);

        // Ticket 3: only the marker; a needs-human notification went out.
        var question = provider.Ticket("3");
        Assert.Equal(TicketStatus.Open, question.Status);
        Assert.Equal(TicketPriority.Low, question.Priority);
        Assert.Equal([TriageConstants.MarkerTag], question.Tags.ToArray());

        Assert.Equal(2, notifications.Sent.Count);
        Assert.Contains(notifications.Sent,
            n => n.Severity == NotificationSeverity.Warning && n.Metadata!["ticketId"] == "1");
        Assert.Contains(notifications.Sent,
            n => n.Severity == NotificationSeverity.Info && n.Metadata!["ticketId"] == "3");

        // A second sweep is a no-op: everything carries the marker.
        await sweep.RunAsync(CancellationToken.None);
        Assert.Equal(2, notifications.Sent.Count);
    }
}
