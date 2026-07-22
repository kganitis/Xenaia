using System.Globalization;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>
/// Accumulates availability sheet write-backs across one drain cycle and
/// flushes them per spreadsheet (spec 6.1 step 3). Two kinds of write:
/// <list type="bullet">
/// <item>patch-status: a single status cell on the patch sheet (the range the
/// caller supplied), set to a success or error string.</item>
/// <item>get-row: vacancies / timestamp / stop-sales for a resolved row on the
/// get sheet. The row is resolved at flush time against the canonical get-sheet
/// layout (see below); a pushed (product, option, timeslot) with no matching
/// row is skipped silently.</item>
/// </list>
/// Canonical get-sheet layout (controller ruling, binding for the whole
/// feature): input columns A = time (HH:mm, blank for slotless), B = product
/// external id, C = option external id, D = participant aliases, E = combination
/// string (<c>productId|optionId|from|to</c>, dates <c>yyyy-MM-dd</c>);
/// write-back columns F = vacancies, G = timestamp, H = stop-sales.
/// The buffer holds no gateway reference: the processor service owns the gateway
/// and calls <see cref="FlushAsync"/>, so a drain with no spreadsheet provider
/// simply never flushes.
/// </summary>
public sealed class SheetWriteBuffer
{
    // Get-sheet input columns A:E are read to resolve a row; the write-back
    // lands on F:H of the matched row.
    private const string InputColumns = "A:E";
    private const string WriteStartColumn = "F"; // vacancies
    private const string WriteEndColumn = "H";   // stop-sales
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    private readonly List<PatchStatusWrite> _patchStatus = [];
    private readonly List<GetRowWrite> _getRows = [];

    /// <summary>The (product, option, timeslot) identity of a get-sheet row.</summary>
    public readonly record struct GetRowKey(
        int ProductExternalId, int OptionExternalId, DateTimeOffset TimeslotAt);

    public IReadOnlyList<PatchStatusWrite> PatchStatusWrites => _patchStatus;

    public IReadOnlyList<GetRowWrite> GetRowWrites => _getRows;

    public bool IsEmpty => _patchStatus.Count == 0 && _getRows.Count == 0;

    /// <summary>Queues a status-cell write on the patch sheet.</summary>
    public void AddPatchStatus(string spreadsheetId, string range, string value)
        => _patchStatus.Add(new PatchStatusWrite(spreadsheetId, range, value));

    /// <summary>Queues a get-sheet write-back for a claimed row's values.</summary>
    public void AddGetRow(
        string spreadsheetId, GetRowKey key,
        int? vacancies, string timestamp, bool? stopSales)
        => _getRows.Add(new GetRowWrite(spreadsheetId, key, vacancies, timestamp, stopSales));

    /// <summary>
    /// Writes every buffered cell through the gateway, one batch per
    /// spreadsheet, then clears the buffer. Get-row keys are resolved against a
    /// freshly read <c>{getSheetName}!A:E</c>; unresolved keys are skipped.
    /// </summary>
    public async Task FlushAsync(
        ISpreadsheetGateway gateway, string getSheetName, CancellationToken ct)
    {
        if (IsEmpty)
            return;

        var spreadsheetIds = _patchStatus.Select(p => p.SpreadsheetId)
            .Concat(_getRows.Select(g => g.SpreadsheetId))
            .Distinct(StringComparer.Ordinal);

        foreach (var spreadsheetId in spreadsheetIds)
        {
            var updates = new List<SheetValueRange>();

            foreach (var patch in _patchStatus.Where(p => p.SpreadsheetId == spreadsheetId))
                updates.Add(new SheetValueRange(NormalizeSingleCell(patch.Range), [[patch.Value]]));

            var getWrites = _getRows.Where(g => g.SpreadsheetId == spreadsheetId).ToList();
            if (getWrites.Count > 0)
            {
                var rows = await ReadGetSheetRowsAsync(gateway, spreadsheetId, getSheetName, ct);
                foreach (var write in getWrites)
                {
                    var row = ResolveRow(rows, write.Key);
                    if (row is null)
                        continue; // no matching row: skip the get write-back silently
                    updates.Add(new SheetValueRange(
                        $"{getSheetName}!{WriteStartColumn}{row}:{WriteEndColumn}{row}",
                        [[
                            write.Vacancies?.ToString() ?? "",
                            write.Timestamp,
                            write.StopSales?.ToString() ?? "",
                        ]]));
                }
            }

            if (updates.Count > 0)
                await gateway.BatchUpdateAsync(spreadsheetId, updates, ct);
        }

        _patchStatus.Clear();
        _getRows.Clear();
    }

    /// <summary>
    /// The first get-sheet row whose product and option match the key, whose
    /// combination range covers the timeslot's date, and whose time-of-day
    /// agrees (a blank A cell matches only the 00:00 slotless sentinel).
    /// Returns the 1-based sheet row number, or null when nothing matches.
    /// </summary>
    private static int? ResolveRow(IReadOnlyList<GetSheetRow> rows, GetRowKey key)
    {
        var date = DateOnly.FromDateTime(key.TimeslotAt.Date);
        var timeOfDay = TimeOnly.FromTimeSpan(key.TimeslotAt.TimeOfDay);
        var slotless = timeOfDay == TimeOnly.MinValue;

        foreach (var row in rows)
        {
            if (row.Product != key.ProductExternalId || row.Option != key.OptionExternalId)
                continue;
            if (date < row.From || date > row.To)
                continue;
            var timeMatches = slotless ? row.Time is null : row.Time == timeOfDay;
            if (timeMatches)
                return row.RowNumber;
        }

        return null;
    }

    private static async Task<List<GetSheetRow>> ReadGetSheetRowsAsync(
        ISpreadsheetGateway gateway, string spreadsheetId, string getSheetName, CancellationToken ct)
    {
        var parsed = new List<GetSheetRow>();
        var rows = await gateway.GetValuesAsync(spreadsheetId, $"{getSheetName}!{InputColumns}", ct);

        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            if (cells.Count < 5)
                continue; // need at least through column E (the combination)
            if (!int.TryParse(cells[1], out var product) || !int.TryParse(cells[2], out var option))
                continue;

            var combination = cells[4].Split('|');
            if (combination.Length < 4)
                continue;
            if (!DateOnly.TryParseExact(combination[2], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                continue;
            if (!DateOnly.TryParseExact(combination[3], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
                continue;

            TimeOnly? time = null;
            var timeCell = cells[0].Trim();
            if (timeCell.Length > 0)
            {
                if (!TimeOnly.TryParseExact(timeCell, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    continue; // malformed time: skip the row
                time = t;
            }

            // Row numbers are 1-based and A:E starts at row 1, so the i-th
            // returned row is sheet row i+1.
            parsed.Add(new GetSheetRow(i + 1, product, option, time, from, to));
        }

        return parsed;
    }

    /// <summary>The in-memory gateway's A1 parser needs an explicit end cell,
    /// so a bare single cell (Tab!B5) is rewritten to the colon form
    /// (Tab!B5:B5); ranges that already contain a colon pass through.</summary>
    private static string NormalizeSingleCell(string range)
    {
        var bang = range.IndexOf('!');
        var cells = bang < 0 ? range : range[(bang + 1)..];
        if (cells.Contains(':'))
            return range;
        return $"{range}:{cells}";
    }

    private readonly record struct GetSheetRow(
        int RowNumber, int Product, int Option, TimeOnly? Time, DateOnly From, DateOnly To);

    public readonly record struct PatchStatusWrite(string SpreadsheetId, string Range, string Value);

    public readonly record struct GetRowWrite(
        string SpreadsheetId, GetRowKey Key, int? Vacancies, string Timestamp, bool? StopSales);
}
