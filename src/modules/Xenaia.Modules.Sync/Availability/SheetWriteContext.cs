namespace Xenaia.Modules.Sync.Availability;

/// <summary>Sheet write-back coordinates for one availability work item.
/// GetRowRange stays null until Task 9's flush resolves get-sheet rows.</summary>
public sealed record SheetWriteContext(
    string SpreadsheetId, string? PatchStatusRange, string? GetRowRange);
