using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.PortContracts.Helpdesk;

/// <summary>
/// Reference IHelpdeskProvider used by module tests and as the contract
/// suite's baseline implementation.
/// </summary>
public sealed class InMemoryHelpdeskProvider : IHelpdeskProvider
{
    private readonly Dictionary<string, HelpdeskTicket> _tickets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _notes = new(StringComparer.Ordinal);

    public InMemoryHelpdeskProvider(IEnumerable<HelpdeskTicket> seed)
    {
        foreach (var ticket in seed)
        {
            _tickets[ticket.Id] = ticket;
            _notes[ticket.Id] = [];
        }
    }

    public HelpdeskTicket Ticket(string id) => _tickets[id];

    public IReadOnlyList<string> Notes(string id) => _notes[id];

    public Task<IReadOnlyList<HelpdeskTicket>> GetOpenTicketsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HelpdeskTicket>>(_tickets.Values
            .Where(t => t.Status == TicketStatus.Open)
            .OrderBy(t => t.CreatedAt)
            .ToList());

    public Task UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken ct)
    {
        if (!_tickets.TryGetValue(ticketId, out var ticket))
            throw new HelpdeskTicketNotFoundException(ticketId);

        var tags = ticket.Tags.ToList();
        foreach (var tag in update.AddTags)
        {
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }

        var fields = new Dictionary<string, string>(ticket.CustomFields);
        foreach (var (name, value) in update.SetCustomFields)
            fields[name] = value;

        _tickets[ticketId] = ticket with
        {
            Status = update.Status ?? ticket.Status,
            Priority = update.Priority ?? ticket.Priority,
            Tags = tags,
            CustomFields = fields,
        };
        return Task.CompletedTask;
    }

    public Task AddPrivateNoteAsync(string ticketId, string htmlBody, CancellationToken ct)
    {
        if (!_notes.TryGetValue(ticketId, out var notes))
            throw new HelpdeskTicketNotFoundException(ticketId);
        notes.Add(htmlBody);
        return Task.CompletedTask;
    }
}
