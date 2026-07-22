using System.Globalization;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>One physical get-sheet row's declared time-of-day within a
/// combination block, keyed by its 1-based sheet row number so the fetch
/// service can write F/G back to the exact row it read. Null time is the
/// blank-column-A case (merged continuation rows and slotless products both
/// arrive this way; the fetch service treats it as the slotless sentinel).</summary>
public sealed record SheetTimeslotRow(int RowNumber, TimeOnly? Time);

/// <summary>One parsed combination block: the (product, option, from, to)
/// identity carried by column E, plus every timeslot row that fell under it
/// (including continuation rows where E arrived blank and was carried
/// forward from the block's first row).</summary>
public sealed record SheetCombination(
    int ProductExternalId, int OptionExternalId, DateOnly From, DateOnly To,
    IReadOnlyList<SheetTimeslotRow> Timeslots);

/// <summary>The parser's full result: every combination block found, the raw
/// row count read from the sheet, and how many rows carried an unparseable
/// combination string.</summary>
public sealed record SheetParseResult(
    int TotalRows, IReadOnlyList<SheetCombination> Combinations, int BadRows);

/// <summary>
/// Parses the get-sheet's <c>A:G</c> values (spec 6.2 step 2; canonical
/// get-sheet layout: A time, B product external id, C option
/// external id, D participant aliases, E combination string
/// <c>productId|optionId|from|to</c>, dates <c>yyyy-MM-dd</c>). Only columns
/// A and E matter here: B/C/D are read back by other flows, and E already
/// carries the product/option identity this parser needs. Merged cells leave
/// column E blank on continuation rows, so this parser carries the last seen
/// E value forward and groups every row under it into one combination block.
/// Rows before the first valid combination, and rows following a malformed
/// one until the next valid combination, are skipped (there is nothing to
/// attribute them to); a malformed combination string is reported as a bad
/// row rather than thrown.
/// </summary>
public sealed class SheetCombinationParser
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    public SheetParseResult Parse(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var combinations = new List<Builder>();
        var badRows = 0;
        Builder? current = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 1;
            var cells = rows[i];
            var combinationCell = Cell(cells, 4).Trim();

            if (combinationCell.Length > 0)
            {
                if (TryParseCombination(combinationCell, out var product, out var option, out var from, out var to))
                {
                    current = new Builder(product, option, from, to);
                    combinations.Add(current);
                }
                else
                {
                    // Malformed: reported, not thrown. Nothing downstream can be
                    // attributed to it (we don't know its product/option/dates),
                    // so it also clears the carry-forward state.
                    badRows++;
                    current = null;
                    continue;
                }
            }

            if (current is null)
                continue; // before the first combination, or right after a bad one

            var timeCell = Cell(cells, 0).Trim();
            TimeOnly? time = null;
            if (timeCell.Length > 0)
            {
                if (!TimeOnly.TryParseExact(
                        timeCell, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
                {
                    badRows++;
                    continue;
                }
                time = parsedTime;
            }

            current.Timeslots.Add(new SheetTimeslotRow(rowNumber, time));
        }

        IReadOnlyList<SheetCombination> result = combinations
            .Select(b => new SheetCombination(b.ProductExternalId, b.OptionExternalId, b.From, b.To, b.Timeslots))
            .ToList();
        return new SheetParseResult(rows.Count, result, badRows);
    }

    private static bool TryParseCombination(
        string value, out int product, out int option, out DateOnly from, out DateOnly to)
    {
        product = 0;
        option = 0;
        from = default;
        to = default;

        var parts = value.Split('|');
        if (parts.Length != 4)
            return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out product))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out option))
            return false;
        if (!DateOnly.TryParseExact(parts[2], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
            return false;
        if (!DateOnly.TryParseExact(parts[3], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
            return false;
        return true;
    }

    private static string Cell(IReadOnlyList<string> cells, int index) =>
        index < cells.Count ? cells[index] : "";

    private sealed class Builder(int productExternalId, int optionExternalId, DateOnly from, DateOnly to)
    {
        public int ProductExternalId { get; } = productExternalId;
        public int OptionExternalId { get; } = optionExternalId;
        public DateOnly From { get; } = from;
        public DateOnly To { get; } = to;
        public List<SheetTimeslotRow> Timeslots { get; } = [];
    }
}
