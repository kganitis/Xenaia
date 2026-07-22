namespace Xenaia.Domain.Bookings.Providers;

/// <summary>Null Vacancies or StopSales means "leave that signal unchanged",
/// matching the Availability aggregate's partial-update semantics. Null Times
/// means the update applies to the whole From/To range (slotless products).</summary>
public sealed record AvailabilityUpdate(
    int ProductExternalId,
    int OptionExternalId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<TimeOnly>? Times,
    int? Vacancies,
    bool? StopSales,
    IReadOnlyList<string> ParticipantTypeAliases);
