namespace Xenaia.Modules.Triage.Helpdesk;

/// <summary>
/// A partial ticket mutation. AddTags appends without duplicating and never
/// removes existing tags; SetCustomFields upserts only the named fields.
/// </summary>
public sealed record TicketUpdate
{
    public TicketStatus? Status { get; init; }
    public TicketPriority? Priority { get; init; }
    public IReadOnlyList<string> AddTags { get; init; } = [];
    public IReadOnlyDictionary<string, string> SetCustomFields { get; init; }
        = new Dictionary<string, string>();
}
