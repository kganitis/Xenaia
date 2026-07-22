using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests.Availability;

public class SheetWriteBufferTests
{
    private const string SpreadsheetId = "ss-1";
    private const string GetSheet = "GetSheet";
    private static readonly DateTimeOffset Timeslot =
        new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Flush_writes_the_patch_status_cell_and_resolves_the_get_row()
    {
        var gateway = new InMemorySpreadsheetGateway();
        // Get sheet: header at row 1, the key (product, option, timeslot) at row 2 in B:D.
        await SeedGetRow(gateway, row: 2, product: 100, option: 7, Timeslot);

        var buffer = new SheetWriteBuffer();
        buffer.AddPatchStatus(SpreadsheetId, "PatchSheet!C3", "Synced at now"); // bare single cell (trap 2)
        buffer.AddGetRow(
            SpreadsheetId, new SheetWriteBuffer.GetRowKey(100, 7, Timeslot),
            vacancies: 12, timestamp: "2026-08-01 10:00 +01:00", stopSales: true);

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        // Patch-status single cell landed (the buffer rewrote it to colon form).
        var patch = await gateway.GetValuesAsync(SpreadsheetId, "PatchSheet!C3:C3", CancellationToken.None);
        Assert.Equal("Synced at now", Assert.Single(Assert.Single(patch)));

        // Get-row write-back landed on the resolved row's E:G columns.
        var written = await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!E2:G2", CancellationToken.None);
        var cells = Assert.Single(written);
        Assert.Equal(["12", "2026-08-01 10:00 +01:00", "True"], cells);

        Assert.True(buffer.IsEmpty); // buffer cleared after flush
    }

    [Fact]
    public async Task Flush_skips_get_rows_whose_key_has_no_matching_sheet_row()
    {
        var gateway = new InMemorySpreadsheetGateway();
        await SeedGetRow(gateway, row: 2, product: 100, option: 7, Timeslot);

        var buffer = new SheetWriteBuffer();
        // A key the sheet does not carry (different option): must be skipped silently.
        buffer.AddGetRow(
            SpreadsheetId, new SheetWriteBuffer.GetRowKey(100, 999, Timeslot),
            vacancies: 4, timestamp: "ts", stopSales: false);
        buffer.AddPatchStatus(SpreadsheetId, "PatchSheet!C3:C3", "ok");

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        // No E:G write happened for the unmatched key.
        var written = await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!E2:G2", CancellationToken.None);
        Assert.Empty(written);
        // The patch-status cell still landed.
        var patch = await gateway.GetValuesAsync(SpreadsheetId, "PatchSheet!C3:C3", CancellationToken.None);
        Assert.Equal("ok", Assert.Single(Assert.Single(patch)));
    }

    private static Task SeedGetRow(
        InMemorySpreadsheetGateway gateway, int row, int product, int option, DateTimeOffset timeslot)
        => gateway.BatchUpdateAsync(
            SpreadsheetId,
            [new SheetValueRange(
                $"{GetSheet}!B{row}:D{row}",
                [[
                    product.ToString(),
                    option.ToString(),
                    timeslot.DateTime.ToString(SheetWriteBuffer.TimeslotKeyFormat, System.Globalization.CultureInfo.InvariantCulture),
                ]])],
            CancellationToken.None);
}
