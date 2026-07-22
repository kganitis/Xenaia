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
/// get sheet. The row is resolved at flush time by reading
/// <c>GetSheetName!B:D</c> once per spreadsheet and matching the
/// (product, option, timeslot) key; keys with no matching row are skipped
/// silently.</item>
/// </list>
/// The buffer holds no gateway reference: the processor service owns the
/// gateway and calls <see cref="FlushAsync"/>, so a drain with no spreadsheet
/// provider simply never flushes.
/// </summary>
public sealed class SheetWriteBuffer
{
    // Get-sheet layout: B:D carry the (product, option, timeslot) key, so the
    // write-back columns start at E. Timeslots are matched on this invariant
    // round-trip format.
    private const int KeyStartColumn = 2;      // column B
    private const int WriteStartColumn = 5;    // column E
    internal const string TimeslotKeyFormat = "yyyy-MM-ddTHH:mm:ss";

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
    /// spreadsheet, then clears the buffer. Get-row keys are resolved against
    /// a freshly read <c>{getSheetName}!B:D</c>; unresolved keys are skipped.
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
                var rowByKey = await BuildRowLookupAsync(gateway, spreadsheetId, getSheetName, ct);
                foreach (var write in getWrites)
                {
                    if (!rowByKey.TryGetValue(TokenFor(write.Key), out var row))
                        continue; // missing key: skip the get write-back silently
                    updates.Add(new SheetValueRange(
                        $"{getSheetName}!{ColumnLetter(WriteStartColumn)}{row}:{ColumnLetter(WriteStartColumn + 2)}{row}",
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

    private static async Task<Dictionary<string, int>> BuildRowLookupAsync(
        ISpreadsheetGateway gateway, string spreadsheetId, string getSheetName, CancellationToken ct)
    {
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = await gateway.GetValuesAsync(
            spreadsheetId, $"{getSheetName}!{ColumnLetter(KeyStartColumn)}:{ColumnLetter(KeyStartColumn + 2)}", ct);

        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            if (cells.Count < 3)
                continue;
            if (!int.TryParse(cells[0], out var product) || !int.TryParse(cells[1], out var option))
                continue;
            if (!DateTime.TryParse(
                    cells[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeslot))
                continue;

            // Row numbers are 1-based and B:D starts at row 1, so the i-th
            // returned row is sheet row i+1.
            lookup[Token(product, option, timeslot)] = i + 1;
        }

        return lookup;
    }

    /// <summary>Matching token for a buffered key: product, option, and the
    /// timeslot's wall-clock instant in the invariant round-trip format. Both
    /// the sheet's D cell and the aggregate's TimeslotAt normalize to this, so
    /// the match is offset-insensitive.</summary>
    private static string TokenFor(GetRowKey key)
        => Token(key.ProductExternalId, key.OptionExternalId, key.TimeslotAt.DateTime);

    private static string Token(int product, int option, DateTime timeslot)
        => $"{product}|{option}|{timeslot.ToString(TimeslotKeyFormat, CultureInfo.InvariantCulture)}";

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

    private static string ColumnLetter(int oneBasedColumn)
    {
        var letters = "";
        var n = oneBasedColumn;
        while (n > 0)
        {
            var rem = (n - 1) % 26;
            letters = (char)('A' + rem) + letters;
            n = (n - 1) / 26;
        }
        return letters;
    }

    public readonly record struct PatchStatusWrite(string SpreadsheetId, string Range, string Value);

    public readonly record struct GetRowWrite(
        string SpreadsheetId, GetRowKey Key, int? Vacancies, string Timestamp, bool? StopSales);
}
