using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.BusinessHours;
using Xenaia.Core.Notifications;
using Xenaia.Core.Tenancy;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xunit;

namespace Xenaia.Modules.Triage.Tests.Processing;

public class BookingUrgencyProcessorTests
{
    private sealed class OpenAlways : IBusinessHoursService
    {
        public bool Open { get; init; } = true;
        public bool IsOpenNow() => Open;
        public bool IsOpenAt(DateTimeOffset instant) => Open;
        public DateTimeOffset? NextOpeningAfter(DateTimeOffset instant) => null;
    }

    private sealed class CollectingNotifications : INotificationService
    {
        public List<Notification> Sent { get; } = [];
        public Task SendAsync(Notification notification, CancellationToken ct = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 12, 14, 0, 0, TimeSpan.Zero);

    private static (BookingUrgencyProcessor Processor, CollectingNotifications Notifications) Build(
        bool open = true, int proximityHours = 5)
    {
        var notifications = new CollectingNotifications();
        var processor = new BookingUrgencyProcessor(
            Options.Create(new TriageOptions
            {
                RulePackPath = "unused.yaml",
                Urgency = new UrgencyOptions { ProximityHours = proximityHours },
            }),
            Options.Create(new TenantProfileOptions { TimeZone = "America/New_York" }),
            new OpenAlways { Open = open },
            notifications,
            new FakeTimeProvider(Now),
            NullLogger<BookingUrgencyProcessor>.Instance);
        return (processor, notifications);
    }

    private static TriageContext Context(params (string Key, string Value)[] captures) => new(
        new HelpdeskTicket { Id = "7" },
        "new-booking",
        captures.ToDictionary(c => c.Key, c => c.Value),
        new TicketUpdateDraft());

    [Fact]
    public async Task Escalates_a_booking_inside_the_window()
    {
        var (processor, notifications) = Build();
        // 12:30 New York time is 16:30 UTC, 2.5 hours from Now.
        var context = Context(("bookingDateTime", "12/08/2026"), ("time", "12:30"),
            ("bookingCode", "MT-7Q2K9F4A"));

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Equal(TicketStatus.Open, context.Draft.Status);
        Assert.Equal(TicketPriority.Urgent, context.Draft.Priority);
        var notification = Assert.Single(notifications.Sent);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Equal("7", notification.Metadata!["ticketId"]);
        Assert.Equal("MT-7Q2K9F4A", notification.Metadata!["bookingCode"]);
    }

    [Fact]
    public async Task Skips_a_booking_beyond_the_proximity_threshold()
    {
        var (processor, notifications) = Build();
        var context = Context(("bookingDateTime", "20/08/2026"), ("time", "12:30"));

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Null(context.Draft.Priority);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Skips_a_booking_in_the_past()
    {
        var (processor, notifications) = Build();
        var context = Context(("bookingDateTime", "12/08/2026"), ("time", "09:00"));

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Null(context.Draft.Priority);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Skips_when_business_hours_are_closed()
    {
        var (processor, notifications) = Build(open: false);
        var context = Context(("bookingDateTime", "12/08/2026"), ("time", "12:30"));

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Null(context.Draft.Priority);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Skips_quietly_when_the_capture_is_absent()
    {
        var (processor, notifications) = Build();
        var context = Context();

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Null(context.Draft.Priority);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Skips_quietly_when_the_value_parses_with_no_format()
    {
        var (processor, notifications) = Build();
        var context = Context(("bookingDateTime", "sometime soon"));

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Null(context.Draft.Priority);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Later_formats_in_the_list_are_tried_in_order()
    {
        var notifications = new CollectingNotifications();
        var processor = new BookingUrgencyProcessor(
            Options.Create(new TriageOptions
            {
                RulePackPath = "unused.yaml",
                Urgency = new UrgencyOptions
                {
                    DateTimeFormats = ["yyyy-MM-dd HH:mm", "dd/MM/yyyy HH:mm"],
                },
            }),
            Options.Create(new TenantProfileOptions { TimeZone = "America/New_York" }),
            new OpenAlways(),
            notifications,
            new FakeTimeProvider(Now),
            NullLogger<BookingUrgencyProcessor>.Instance);
        var context = Context(("bookingDateTime", "12/08/2026"), ("time", "12:30"));

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Equal(TicketPriority.Urgent, context.Draft.Priority);
    }

    [Fact]
    public async Task Tenant_timezone_offset_is_respected_across_seasons()
    {
        // Winter clock: 2026-12-16T14:00Z is 09:00 EST (UTC-5). A 10:30 local
        // booking is 15:30 UTC, 1.5 hours out: inside the window only if the
        // EST offset was applied (with the summer EDT offset it would be 14:30
        // UTC and 0.5 hours out, still inside, so assert the exact instant).
        var notifications = new CollectingNotifications();
        var processor = new BookingUrgencyProcessor(
            Options.Create(new TriageOptions { RulePackPath = "unused.yaml" }),
            Options.Create(new TenantProfileOptions { TimeZone = "America/New_York" }),
            new OpenAlways(),
            notifications,
            new FakeTimeProvider(new DateTimeOffset(2026, 12, 16, 14, 0, 0, TimeSpan.Zero)),
            NullLogger<BookingUrgencyProcessor>.Instance);
        var context = Context(("bookingDateTime", "16/12/2026"), ("time", "10:30"));

        await processor.ProcessAsync(context, CancellationToken.None);

        var notification = Assert.Single(notifications.Sent);
        Assert.Equal("2026-12-16T15:30:00.0000000Z", notification.Metadata!["bookingStartUtc"]);
    }
}
