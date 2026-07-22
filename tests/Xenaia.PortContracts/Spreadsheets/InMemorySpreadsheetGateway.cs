using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.PortContracts.Spreadsheets;

/// <summary>
/// Reference ISpreadsheetGateway used by module tests and as the contract
/// suite's baseline implementation. A spreadsheetId maps to tabs, each a
/// sparse (row, col) -> cell-value grid behind a lock; not meant for
/// concurrency stress, only for deterministic, inspectable test fixtures.
/// </summary>
public sealed class InMemorySpreadsheetGateway : ISpreadsheetGateway
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, Dictionary<string, Dictionary<(int Row, int Col), string>>> _spreadsheets =
        new(StringComparer.Ordinal);

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetValuesAsync(
        string spreadsheetId, string range, CancellationToken ct)
    {
        var (tab, startCol, startRow, endCol, endRow) = ParseRange(range);

        lock (_lock)
        {
            var tabCells = GetTabCells(spreadsheetId, tab, createIfMissing: false);

            var effectiveStartRow = startRow ?? 1;
            var effectiveEndRow = endRow ??
                (tabCells is { Count: > 0 } ? tabCells.Keys.Max(k => k.Row) : effectiveStartRow - 1);

            var rows = new List<IReadOnlyList<string>>();
            for (var row = effectiveStartRow; row <= effectiveEndRow; row++)
            {
                var rowValues = new List<string>();
                for (var col = startCol; col <= endCol; col++)
                    rowValues.Add(tabCells is not null && tabCells.TryGetValue((row, col), out var value) ? value : "");

                while (rowValues.Count > 0 && rowValues[^1].Length == 0)
                    rowValues.RemoveAt(rowValues.Count - 1);

                rows.Add(rowValues);
            }

            // Trailing rows with no data at all are omitted (Sheets semantics);
            // an empty/unknown tab thus yields an empty list, not N empty rows.
            while (rows.Count > 0 && rows[^1].Count == 0)
                rows.RemoveAt(rows.Count - 1);

            return Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>(rows);
        }
    }

    public Task BatchUpdateAsync(
        string spreadsheetId, IReadOnlyList<SheetValueRange> updates, CancellationToken ct)
    {
        lock (_lock)
        {
            foreach (var update in updates)
            {
                var (tab, startCol, startRow, _, _) = ParseRange(update.Range);
                var tabCells = GetTabCells(spreadsheetId, tab, createIfMissing: true)!;
                var rowOrigin = startRow ?? 1;

                for (var ri = 0; ri < update.Rows.Count; ri++)
                {
                    var rowData = update.Rows[ri];
                    for (var ci = 0; ci < rowData.Count; ci++)
                        tabCells[(rowOrigin + ri, startCol + ci)] = rowData[ci];
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task EnsureTabAsync(string spreadsheetId, string title, CancellationToken ct)
    {
        lock (_lock)
        {
            GetTabCells(spreadsheetId, title, createIfMissing: true);
        }
        return Task.CompletedTask;
    }

    private Dictionary<(int Row, int Col), string>? GetTabCells(
        string spreadsheetId, string tab, bool createIfMissing)
    {
        if (!_spreadsheets.TryGetValue(spreadsheetId, out var tabs))
        {
            if (!createIfMissing)
                return null;
            tabs = new Dictionary<string, Dictionary<(int, int), string>>(StringComparer.Ordinal);
            _spreadsheets[spreadsheetId] = tabs;
        }

        if (!tabs.TryGetValue(tab, out var cells))
        {
            if (!createIfMissing)
                return null;
            cells = [];
            tabs[tab] = cells;
        }

        return cells;
    }

    /// <summary>Parses a simple A1 range such as "Tab!A1:C3" or "Tab!A:G".
    /// The latter form (no row digits) leaves the row bounds null, meaning
    /// "all present rows" to the caller.</summary>
    private static (string Tab, int StartCol, int? StartRow, int EndCol, int? EndRow) ParseRange(string range)
    {
        var bang = range.IndexOf('!');
        if (bang < 0)
            throw new FormatException($"Range '{range}' must be in 'Tab!A1:C3' form.");

        var tab = UnquoteTab(range[..bang]);
        var cellsPart = range[(bang + 1)..];
        var colon = cellsPart.IndexOf(':');
        if (colon < 0)
            throw new FormatException($"Range '{range}' must specify a start and end (e.g. 'A1:C3' or 'A:G').");

        var (startCol, startRow) = ParseCell(cellsPart[..colon]);
        var (endCol, endRow) = ParseCell(cellsPart[(colon + 1)..]);
        return (tab, startCol, startRow, endCol, endRow);
    }

    /// <summary>Strips the single quotes an A1 tab name carries when it holds
    /// spaces or other non-plain characters, unescaping a doubled inner quote
    /// back to one; a plain unquoted name passes through unchanged.</summary>
    private static string UnquoteTab(string tab)
    {
        if (tab.Length >= 2 && tab[0] == '\'' && tab[^1] == '\'')
            return tab[1..^1].Replace("''", "'");
        return tab;
    }

    private static (int Col, int? Row) ParseCell(string cell)
    {
        var i = 0;
        while (i < cell.Length && char.IsLetter(cell[i]))
            i++;

        var col = ColumnLettersToIndex(cell[..i]);
        var rowDigits = cell[i..];
        int? row = rowDigits.Length == 0 ? null : int.Parse(rowDigits);
        return (col, row);
    }

    private static int ColumnLettersToIndex(string letters)
    {
        var index = 0;
        foreach (var ch in letters)
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return index;
    }
}
