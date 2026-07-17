using Microsoft.Extensions.Logging;
using Xenaia.Core.Notifications;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Processing;

/// <summary>
/// One triage pass: fetch open tickets, skip already-marked ones, categorize,
/// act, stamp the marker. Notes go out before the single update so the marker
/// rides the final call: a failure anywhere leaves the ticket unmarked and it
/// is retried next poll (at-least-once; the note is the only duplicable step).
/// </summary>
public sealed class TriageSweep(
    IHelpdeskProvider helpdesk,
    IRulePackProvider rulePackProvider,
    RuleEvaluator evaluator,
    IEnumerable<ITicketProcessor> processors,
    INotificationService notifications,
    ILogger<TriageSweep> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var pack = rulePackProvider.Pack;
        var processorsByName = processors.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var tickets = await helpdesk.GetOpenTicketsAsync(ct);
        logger.LogInformation("Triage sweep: {Count} open tickets fetched", tickets.Count);

        foreach (var ticket in tickets)
        {
            if (ticket.Tags.Contains(TriageConstants.MarkerTag, StringComparer.OrdinalIgnoreCase))
                continue;
            try
            {
                await TriageTicketAsync(pack, processorsByName, ticket, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to triage ticket {TicketId}; it stays unmarked and retries next poll",
                    ticket.Id);
            }
        }
    }

    private async Task TriageTicketAsync(
        RulePack pack,
        IReadOnlyDictionary<string, ITicketProcessor> processorsByName,
        HelpdeskTicket ticket,
        CancellationToken ct)
    {
        var fields = new TicketFields(
            ticket.Subject, TextNormalizer.Normalize(ticket.BodyHtml), ticket.Sender, ticket.Channel);
        var match = evaluator.Evaluate(pack, fields);
        var draft = new TicketUpdateDraft();

        if (match is null)
        {
            logger.LogInformation(
                "Ticket {TicketId} matched no rule; categorized as {Category}",
                ticket.Id, pack.UnmatchedCategory);
            var subject = Truncate(ticket.Subject, 120);
            await notifications.SendAsync(new Notification(
                $"Ticket {ticket.Id} needs human triage",
                subject,
                NotificationSeverity.Info,
                new Dictionary<string, string> { ["ticketId"] = ticket.Id, ["subject"] = subject }), ct);
        }
        else
        {
            logger.LogInformation(
                "Ticket {TicketId} categorized as {Category} by rule {RuleId}",
                ticket.Id, match.Rule.Category, match.Rule.Id);
            ActionFolder.Fold(match.Rule.Actions, match.Captures, draft);
            if (match.Rule.ProcessorName is not null)
            {
                var context = new TriageContext(ticket, match.Rule.Category, match.Captures, draft);
                await processorsByName[match.Rule.ProcessorName].ProcessAsync(context, ct);
            }
        }

        foreach (var note in draft.Notes)
            await helpdesk.AddPrivateNoteAsync(ticket.Id, note, ct);

        draft.AddTag(TriageConstants.MarkerTag);
        await helpdesk.UpdateTicketAsync(ticket.Id, draft.ToTicketUpdate(), ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
