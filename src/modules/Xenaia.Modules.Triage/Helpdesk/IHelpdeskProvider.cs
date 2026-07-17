namespace Xenaia.Modules.Triage.Helpdesk;

/// <summary>
/// The helpdesk port, owned by Triage and shaped by what Triage consumes.
/// Contract semantics are asserted by HelpdeskProviderContract in
/// tests/Xenaia.PortContracts; every adapter must pass that suite.
/// </summary>
public interface IHelpdeskProvider
{
    /// <summary>Open tickets, oldest first. May include already-triaged
    /// tickets; the caller filters by marker tag.</summary>
    Task<IReadOnlyList<HelpdeskTicket>> GetOpenTicketsAsync(CancellationToken ct);

    /// <summary>Throws HelpdeskTicketNotFoundException for an unknown id.</summary>
    Task UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken ct);

    Task AddPrivateNoteAsync(string ticketId, string htmlBody, CancellationToken ct);
}
