using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Modules.Triage.Processing;

/// <summary>Everything a coded processor sees for one categorized ticket.</summary>
public sealed record TriageContext(
    HelpdeskTicket Ticket,
    string Category,
    IReadOnlyDictionary<string, string> Captures,
    TicketUpdateDraft Draft);
