namespace Xenaia.Modules.Sync.Availability;

/// <summary>
/// Outcome of one <see cref="AvailabilityFetchService.SyncFromSheetAsync"/>
/// call (spec 6.2): total get-sheet rows read, distinct combinations parsed
/// out of column E, how many of those combinations reached the vendor
/// successfully versus not, how many timeslot rows actually received a fresh
/// or preserved vacancies value, and how many timeslot rows were declared on
/// the sheet but absent from an otherwise successful vendor response.
/// </summary>
public sealed record SheetSyncSummary(
    int TotalRows, int Combinations, int SuccessfulFetches,
    int FailedFetches, int RowsUpdated, int TimeslotsNotFound);
