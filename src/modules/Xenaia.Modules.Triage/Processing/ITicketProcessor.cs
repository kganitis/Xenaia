namespace Xenaia.Modules.Triage.Processing;

/// <summary>
/// A coded processing hook for logic that cannot be a declarative action.
/// Rules bind to it by name; a rule naming an unregistered processor fails
/// startup (TriageOptionsValidator).
/// </summary>
public interface ITicketProcessor
{
    /// <summary>Stable name rules bind to, e.g. "booking-urgency".</summary>
    string Name { get; }

    Task ProcessAsync(TriageContext context, CancellationToken ct);
}
