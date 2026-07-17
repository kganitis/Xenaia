namespace Xenaia.Modules.Triage.Helpdesk;

public sealed class HelpdeskTicketNotFoundException(string ticketId)
    : Exception($"Helpdesk ticket '{ticketId}' was not found.")
{
    public string TicketId { get; } = ticketId;
}
