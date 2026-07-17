namespace Xenaia.Modules.Triage.Helpdesk;

/// <summary>
/// A provider-agnostic ticket as Triage consumes it. The adapter maps
/// vendor fields to this shape; nothing vendor-specific crosses the port.
/// </summary>
public sealed record HelpdeskTicket
{
    public required string Id { get; init; }
    public string Subject { get; init; } = "";
    public string BodyHtml { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Channel { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public TicketStatus Status { get; init; }
    public TicketPriority Priority { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Canonical field name -> value. The adapter translates vendor
    /// names (cf_booking_code) to canonical names (bookingCode) via its field
    /// map. A field mapped to the canonical name "channel" also populates
    /// Channel.</summary>
    public IReadOnlyDictionary<string, string> CustomFields { get; init; }
        = new Dictionary<string, string>();
}
