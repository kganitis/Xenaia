namespace Xenaia.Modules.Sync.Availability;

/// <summary>Sheet write-back coordinates for one availability work item: the
/// spreadsheet and the optional patch-sheet status cell to update.</summary>
public sealed record SheetWriteContext(
    string SpreadsheetId, string? PatchStatusRange);
