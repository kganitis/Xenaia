using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xenaia.Core.Notifications;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;
using Xenaia.PortContracts.Helpdesk;

namespace Xenaia.Modules.Triage.Tests.Processing;

public class TriageSweepTests
{
    private const string PackYaml = """
        version: 1
        defaults:
          unmatchedCategory: needs-human
        rules:
          - id: review-received
            category: review-received
            match: { subject: 'new review' }
            actions:
              - addTags: [review]
              - setStatus: closed
          - id: payment-received
            category: payment-received
            match: { subject: '^Payment received' }
            extract:
              amount:
                from: body
                pattern: '(?<amount>[0-9]+\.[0-9]{2})'
            actions:
              - addNote: 'Payment of {amount} recorded.'
          - id: escalate-me
            category: new-booking
            match: { subject: '^New Booking' }
            processor: always-urgent
        """;

    private sealed class CollectingNotifications : INotificationService
    {
        public List<Notification> Sent { get; } = [];
        public Task SendAsync(Notification notification, CancellationToken ct = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysUrgentProcessor : ITicketProcessor
    {
        public int Calls { get; private set; }
        public string Name => "always-urgent";
        public Task ProcessAsync(TriageContext context, CancellationToken ct)
        {
            Calls++;
            context.Draft.Priority = TicketPriority.Urgent;
            return Task.CompletedTask;
        }
    }

    /// <summary>Wraps the in-memory provider and fails the first N updates.</summary>
    private sealed class FlakyProvider(InMemoryHelpdeskProvider inner, int failures) : IHelpdeskProvider
    {
        private int _remaining = failures;

        public Task<IReadOnlyList<HelpdeskTicket>> GetOpenTicketsAsync(CancellationToken ct) =>
            inner.GetOpenTicketsAsync(ct);

        public Task UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken ct)
        {
            if (_remaining-- > 0)
                throw new HttpRequestException("transient helpdesk failure");
            return inner.UpdateTicketAsync(ticketId, update, ct);
        }

        public Task AddPrivateNoteAsync(string ticketId, string htmlBody, CancellationToken ct) =>
            inner.AddPrivateNoteAsync(ticketId, htmlBody, ct);
    }

    private sealed class FixedRulePackProvider(RulePack pack) : IRulePackProvider
    {
        public RulePack Pack { get; } = pack;
    }

    private static HelpdeskTicket Ticket(string id, string subject, string body = "") => new()
    {
        Id = id,
        Subject = subject,
        BodyHtml = body,
        Status = TicketStatus.Open,
        CreatedAt = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
    };

    private static (TriageSweep Sweep, CollectingNotifications Notifications, AlwaysUrgentProcessor Processor)
        Build(IHelpdeskProvider provider)
    {
        var notifications = new CollectingNotifications();
        var processor = new AlwaysUrgentProcessor();
        var sweep = new TriageSweep(
            provider,
            new FixedRulePackProvider(RulePackLoader.Parse(PackYaml)),
            new RuleEvaluator(NullLogger<RuleEvaluator>.Instance),
            [processor],
            notifications,
            NullLogger<TriageSweep>.Instance);
        return (sweep, notifications, processor);
    }

    [Fact]
    public async Task Matched_ticket_gets_actions_marker_tag_and_one_update()
    {
        var provider = new InMemoryHelpdeskProvider([Ticket("1", "You have a new review!")]);
        var (sweep, notifications, _) = Build(provider);

        await sweep.RunAsync(CancellationToken.None);

        var ticket = provider.Ticket("1");
        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Contains("review", ticket.Tags);
        Assert.Contains(TriageConstants.MarkerTag, ticket.Tags);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Notes_are_created_with_substituted_captures()
    {
        var provider = new InMemoryHelpdeskProvider(
            [Ticket("2", "Payment received for order A-1", "<p>Total 84.50 EUR</p>")]);
        var (sweep, _, _) = Build(provider);

        await sweep.RunAsync(CancellationToken.None);

        Assert.Equal(["Payment of 84.50 recorded."], provider.Notes("2").ToArray());
        Assert.Contains(TriageConstants.MarkerTag, provider.Ticket("2").Tags);
    }

    [Fact]
    public async Task Bound_processor_runs_and_amends_the_draft()
    {
        var provider = new InMemoryHelpdeskProvider([Ticket("3", "New Booking [MT-7Q2K9F4A]")]);
        var (sweep, _, processor) = Build(provider);

        await sweep.RunAsync(CancellationToken.None);

        Assert.Equal(1, processor.Calls);
        Assert.Equal(TicketPriority.Urgent, provider.Ticket("3").Priority);
    }

    [Fact]
    public async Task Unmatched_ticket_gets_only_the_marker_and_a_needs_human_notification()
    {
        var provider = new InMemoryHelpdeskProvider([Ticket("4", "Do you allow dogs on kayaks?")]);
        var (sweep, notifications, _) = Build(provider);

        await sweep.RunAsync(CancellationToken.None);

        var ticket = provider.Ticket("4");
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal([TriageConstants.MarkerTag], ticket.Tags.ToArray());
        var notification = Assert.Single(notifications.Sent);
        Assert.Contains("needs human", notification.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("4", notification.Metadata!["ticketId"]);
    }

    [Fact]
    public async Task Already_marked_tickets_are_skipped()
    {
        var seeded = Ticket("5", "You have a new review!") with { Tags = [TriageConstants.MarkerTag] };
        var provider = new InMemoryHelpdeskProvider([seeded]);
        var (sweep, notifications, _) = Build(provider);

        await sweep.RunAsync(CancellationToken.None);

        Assert.Equal(TicketStatus.Open, provider.Ticket("5").Status);
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task One_failing_ticket_does_not_halt_the_sweep()
    {
        var inner = new InMemoryHelpdeskProvider(
        [
            Ticket("6", "You have a new review!"),
            Ticket("7", "You have a new review!") with
            {
                CreatedAt = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            },
        ]);
        var (sweep, _, _) = Build(new FlakyProvider(inner, failures: 1));

        await sweep.RunAsync(CancellationToken.None);

        Assert.Equal(TicketStatus.Open, inner.Ticket("6").Status);
        Assert.DoesNotContain(TriageConstants.MarkerTag, inner.Ticket("6").Tags);
        Assert.Equal(TicketStatus.Closed, inner.Ticket("7").Status);
    }
}
