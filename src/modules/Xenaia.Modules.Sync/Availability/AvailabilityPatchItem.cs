namespace Xenaia.Modules.Sync.Availability;

/// <summary>One incoming patch line. Times empty means slotless (whole range).
/// Null Vacancies/StopSales means "don't care" for that signal.</summary>
public sealed record AvailabilityPatchItem(
    int ProductExternalId, int OptionExternalId,
    DateOnly From, DateOnly To, IReadOnlyList<TimeOnly> Times,
    int? Vacancies, bool? StopSales,
    string? PatchStatusRange); // A1 range for status write-back, optional
