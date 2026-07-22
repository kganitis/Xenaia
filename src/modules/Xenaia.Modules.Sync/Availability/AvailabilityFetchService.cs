using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Core.Tenancy;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>
/// Sheet-driven availability fetch (spec 6.2): reads the get-sheet, fetches
/// vendor availability per parsed combination self-throttled by
/// <c>Sync:Availability:FetchDelayMs</c>, upserts timeslots as <c>Synced</c>
/// rows, and writes vacancies (or a diagnostic string) plus a tenant-zoned
/// timestamp back to F/G of every row considered. Never touches column H
/// (stop-sales is Task 9's write-back concern). Stop-sales preservation
/// (spec 6.2): a vendor-reported zero never overwrites the vacancies of a row
/// whose StopSales is already true (the zero is the stop sale, not the
/// capacity). Scoped: a fresh instance per Task 16 endpoint call.
/// </summary>
public sealed class AvailabilityFetchService
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm zzz";
    private const string FetchFailedDiagnostic = "Fetch failed";
    private const string TimeNotFoundDiagnostic = "Time not found";

    private readonly IAvailabilityStore _store;
    private readonly IBookingSystemProvider _provider;
    private readonly ISpreadsheetGateway _gateway;
    private readonly SheetCombinationParser _parser;
    private readonly SyncOptions _options;
    private readonly TenantProfileOptions _tenant;
    private readonly TimeProvider _clock;
    private readonly ILogger<AvailabilityFetchService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayer;

    public AvailabilityFetchService(
        IAvailabilityStore store,
        IBookingSystemProvider provider,
        ISpreadsheetGateway gateway,
        SheetCombinationParser parser,
        IOptions<SyncOptions> options,
        IOptions<TenantProfileOptions> tenantOptions,
        TimeProvider clock,
        ILogger<AvailabilityFetchService> logger,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        _store = store;
        _provider = provider;
        _gateway = gateway;
        _parser = parser;
        _options = options.Value;
        _tenant = tenantOptions.Value;
        _clock = clock;
        _logger = logger;
        _delayer = delayer ?? ((delay, token) => Task.Delay(delay, clock, token));
    }

    /// <summary>Reads <c>{GetSheetName}!A:G</c>, fetches vendor availability
    /// per combination, upserts timeslots, writes vacancies + timestamp back.</summary>
    public async Task<SheetSyncSummary> SyncFromSheetAsync(string spreadsheetId, CancellationToken ct)
    {
        var getSheetName = _options.Availability.GetSheetName;
        var rawRows = await _gateway.GetValuesAsync(spreadsheetId, $"{getSheetName}!A:G", ct);
        var parsed = _parser.Parse(rawRows);

        var successfulFetches = 0;
        var failedFetches = 0;
        var rowsUpdated = 0;
        var timeslotsNotFound = 0;
        var writeBacks = new List<SheetValueRange>();

        for (var i = 0; i < parsed.Combinations.Count; i++)
        {
            if (i > 0)
                await _delayer(TimeSpan.FromMilliseconds(_options.Availability.FetchDelayMs), ct);

            var combination = parsed.Combinations[i];
            var timestamp = FormatTimestamp(_clock.GetUtcNow());
            var fetched = await FetchAsync(combination, ct);

            if (fetched is null)
            {
                failedFetches++;
                foreach (var row in combination.Timeslots)
                    writeBacks.Add(RowWrite(getSheetName, row.RowNumber, FetchFailedDiagnostic, timestamp));
                continue;
            }

            successfulFetches++;

            var matched = new List<(SheetTimeslotRow Row, DateTimeOffset TimeslotAt, int Vacancies)>();
            foreach (var row in combination.Timeslots)
            {
                var timeslotAt = TimeslotAt(combination.From, row.Time);
                var match = fetched.FirstOrDefault(t => t.At == timeslotAt);
                if (match is null)
                {
                    timeslotsNotFound++;
                    writeBacks.Add(RowWrite(getSheetName, row.RowNumber, TimeNotFoundDiagnostic, timestamp));
                    continue;
                }

                matched.Add((row, timeslotAt, match.Vacancies));
            }

            if (matched.Count == 0)
                continue;

            var keys = matched
                .Select(m => new AvailabilityKey(combination.ProductExternalId, combination.OptionExternalId, m.TimeslotAt))
                .Distinct()
                .ToList();
            var existingByKey = (await _store.GetByKeysAsync(keys, ct))
                .ToDictionary(a => new AvailabilityKey(a.ExternalProductId, a.ExternalOptionId, a.TimeslotAt));

            var now = _clock.GetUtcNow();
            foreach (var (row, timeslotAt, vacancies) in matched)
            {
                var key = new AvailabilityKey(combination.ProductExternalId, combination.OptionExternalId, timeslotAt);
                if (!existingByKey.TryGetValue(key, out var aggRow))
                {
                    aggRow = AvailabilityAggregate.ForTimeslot(
                        combination.ProductExternalId, combination.OptionExternalId, timeslotAt);
                    await _store.AddAsync(aggRow, ct);
                    existingByKey[key] = aggRow;
                }

                if (aggRow.Sync.Status == SyncStatus.Processing)
                {
                    // In flight for the outbound pusher: never touch a row a
                    // worker currently owns.
                    _logger.LogDebug(
                        "Availability fetch: row for {Product}/{Option}@{At} is in flight; leaving it untouched",
                        combination.ProductExternalId, combination.OptionExternalId, timeslotAt);
                    continue;
                }

                var preserveStopSales = aggRow.StopSales == true && vacancies == 0;
                if (!preserveStopSales)
                    aggRow.SetVacancies(vacancies);

                if (aggRow.Sync.Status != SyncStatus.Pending)
                    aggRow.RequeueSync();
                aggRow.ClaimForSync();
                aggRow.MarkSynced(now);

                rowsUpdated++;
                var finalVacancies = aggRow.Vacancies ?? vacancies;
                writeBacks.Add(RowWrite(
                    getSheetName, row.RowNumber, finalVacancies.ToString(CultureInfo.InvariantCulture), timestamp));
            }

            await _store.SaveChangesAsync(ct);
        }

        if (writeBacks.Count > 0)
            await _gateway.BatchUpdateAsync(spreadsheetId, writeBacks, ct);

        return new SheetSyncSummary(
            parsed.TotalRows, parsed.Combinations.Count, successfulFetches, failedFetches, rowsUpdated, timeslotsNotFound);
    }

    /// <summary>Null return means "Fetch failed": either the vendor doesn't
    /// know the product/option (GetAvailabilityAsync returned null) or the
    /// call threw.</summary>
    private async Task<IReadOnlyList<AvailabilityTimeslot>?> FetchAsync(
        SheetCombination combination, CancellationToken ct)
    {
        var from = new DateTimeOffset(combination.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(combination.To.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero);

        try
        {
            return await _provider.GetAvailabilityAsync(
                combination.ProductExternalId, combination.OptionExternalId, from, to, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Availability fetch: vendor call threw for product {Product} option {Option}",
                combination.ProductExternalId, combination.OptionExternalId);
            return null;
        }
    }

    /// <summary>A row's date is its combination's From date (spec's two-row
    /// group example groups two times on one date, not a date span); a null
    /// row time is the slotless sentinel, matching midnight.</summary>
    private static DateTimeOffset TimeslotAt(DateOnly date, TimeOnly? time) =>
        new(date.ToDateTime(time ?? TimeOnly.MinValue), TimeSpan.Zero);

    private static SheetValueRange RowWrite(string getSheetName, int rowNumber, string vacanciesCell, string timestamp) =>
        new($"{getSheetName}!F{rowNumber}:G{rowNumber}", [[vacanciesCell, timestamp]]);

    private string FormatTimestamp(DateTimeOffset utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_tenant.TimeZone);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return local.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }
}
