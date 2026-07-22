namespace Xenaia.Modules.Sync.Spreadsheets;

/// <summary>One rectangular block of cell values at an A1-notation range.</summary>
public sealed record SheetValueRange(string Range, IReadOnlyList<IReadOnlyList<string>> Rows);

public interface ISpreadsheetGateway
{
    /// <summary>Values in the A1 range; empty list for an empty range.
    /// Trailing empty cells may be omitted per row (Sheets semantics).</summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> GetValuesAsync(
        string spreadsheetId, string range, CancellationToken ct);

    Task BatchUpdateAsync(
        string spreadsheetId, IReadOnlyList<SheetValueRange> updates, CancellationToken ct);

    /// <summary>Creates the tab if missing; no-op when present.</summary>
    Task EnsureTabAsync(string spreadsheetId, string title, CancellationToken ct);
}
