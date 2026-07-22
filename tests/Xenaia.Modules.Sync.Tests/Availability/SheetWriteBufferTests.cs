using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests.Availability;

public class SheetWriteBufferTests
{
    private const string SpreadsheetId = "ss-1";
    private const string GetSheet = "GetSheet";

    // Canonical get-sheet layout: A = time (HH:mm, blank for slotless),
    // B = product, C = option, D = aliases, E = combination
    // (productId|optionId|from|to, yyyy-MM-dd); write-back F/G/H.

    [Fact]
    public async Task Flush_writes_the_patch_status_cell_and_resolves_the_get_row_on_F_to_H()
    {
        var gateway = new InMemorySpreadsheetGateway();
        await SeedRow(gateway, row: 2, time: "09:00", product: 100, option: 7,
            combination: "100|7|2026-08-01|2026-08-01");

        var buffer = new SheetWriteBuffer();
        buffer.AddPatchStatus(SpreadsheetId, "PatchSheet!C3", "Synced at now"); // bare single cell (trap 2)
        buffer.AddGetRow(
            SpreadsheetId, Key(100, 7, "2026-08-01T09:00:00"),
            vacancies: 12, timestamp: "2026-08-01 10:00 +01:00", stopSales: true);

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        var patch = await gateway.GetValuesAsync(SpreadsheetId, "PatchSheet!C3:C3", CancellationToken.None);
        Assert.Equal("Synced at now", Assert.Single(Assert.Single(patch)));

        var written = await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F2:H2", CancellationToken.None);
        Assert.Equal(["12", "2026-08-01 10:00 +01:00", "True"], Assert.Single(written));

        Assert.True(buffer.IsEmpty); // buffer cleared after flush
    }

    [Fact]
    public async Task Flush_lands_the_write_back_on_the_matching_row_of_a_multi_row_sheet()
    {
        var gateway = new InMemorySpreadsheetGateway();
        await SeedRow(gateway, row: 2, time: "09:00", product: 100, option: 7, combination: "100|7|2026-08-01|2026-08-01");
        await SeedRow(gateway, row: 3, time: "10:00", product: 100, option: 7, combination: "100|7|2026-08-01|2026-08-01");
        await SeedRow(gateway, row: 4, time: "09:00", product: 200, option: 7, combination: "200|7|2026-08-01|2026-08-01");

        var buffer = new SheetWriteBuffer();
        buffer.AddGetRow(SpreadsheetId, Key(100, 7, "2026-08-01T10:00:00"), vacancies: 3, timestamp: "ts", stopSales: false);

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        // Only row 3 (product 100, option 7, 10:00) should carry the write-back.
        Assert.Empty(await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F2:H2", CancellationToken.None));
        Assert.Equal(["3", "ts", "False"],
            Assert.Single(await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F3:H3", CancellationToken.None)));
        Assert.Empty(await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F4:H4", CancellationToken.None));
    }

    [Fact]
    public async Task Flush_matches_a_slotless_key_to_a_row_with_a_blank_time_cell()
    {
        var gateway = new InMemorySpreadsheetGateway();
        await SeedRow(gateway, row: 2, time: "", product: 100, option: 7, combination: "100|7|2026-08-01|2026-08-01");

        var buffer = new SheetWriteBuffer();
        buffer.AddGetRow(SpreadsheetId, Key(100, 7, "2026-08-01T00:00:00"), vacancies: 8, timestamp: "ts", stopSales: null);

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        // The empty stop-sales (null) is written as a blank H cell; the gateway
        // trims that trailing blank on read-back, leaving vacancies + timestamp.
        Assert.Equal(["8", "ts"],
            Assert.Single(await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F2:H2", CancellationToken.None)));
    }

    [Fact]
    public async Task Flush_matches_a_timeslot_date_inside_the_combination_from_to_range()
    {
        var gateway = new InMemorySpreadsheetGateway();
        await SeedRow(gateway, row: 2, time: "09:00", product: 100, option: 7, combination: "100|7|2026-08-01|2026-08-03");

        var buffer = new SheetWriteBuffer();
        buffer.AddGetRow(SpreadsheetId, Key(100, 7, "2026-08-02T09:00:00"), vacancies: 1, timestamp: "ts", stopSales: false);

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        Assert.Equal(["1", "ts", "False"],
            Assert.Single(await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F2:H2", CancellationToken.None)));
    }

    [Fact]
    public async Task Flush_skips_get_rows_whose_key_has_no_matching_sheet_row()
    {
        var gateway = new InMemorySpreadsheetGateway();
        await SeedRow(gateway, row: 2, time: "09:00", product: 100, option: 7, combination: "100|7|2026-08-01|2026-08-01");

        var buffer = new SheetWriteBuffer();
        // A key the sheet does not carry (different option): must be skipped silently.
        buffer.AddGetRow(SpreadsheetId, Key(100, 999, "2026-08-01T09:00:00"), vacancies: 4, timestamp: "ts", stopSales: false);
        buffer.AddPatchStatus(SpreadsheetId, "PatchSheet!C3:C3", "ok");

        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        Assert.Empty(await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F2:H2", CancellationToken.None));
        var patch = await gateway.GetValuesAsync(SpreadsheetId, "PatchSheet!C3:C3", CancellationToken.None);
        Assert.Equal("ok", Assert.Single(Assert.Single(patch)));
    }

    private static SheetWriteBuffer.GetRowKey Key(int product, int option, string timeslot)
        => new(product, option, DateTimeOffset.Parse(timeslot + "+00:00"));

    private static Task SeedRow(
        InMemorySpreadsheetGateway gateway, int row, string time, int product, int option, string combination)
        => gateway.BatchUpdateAsync(
            SpreadsheetId,
            [new SheetValueRange(
                $"{GetSheet}!A{row}:E{row}",
                [[time, product.ToString(), option.ToString(), "adult", combination]])],
            CancellationToken.None);
}
