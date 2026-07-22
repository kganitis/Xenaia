using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests.Spreadsheets;

public class InMemorySpreadsheetGatewayQuotingTests
{
    [Fact]
    public async Task Quoted_spaced_tab_range_addresses_the_same_cells_as_the_raw_tab()
    {
        var gateway = new InMemorySpreadsheetGateway();
        var quotedRange = $"{A1.QuoteTab("My Sheet")}!A1:B1"; // 'My Sheet'!A1:B1

        await gateway.BatchUpdateAsync(
            "ss-1", [new SheetValueRange(quotedRange, [["a", "b"]])], CancellationToken.None);

        // The parser strips the surrounding quotes, so the quoted read and a
        // raw-name read hit the same tab and cells.
        var quoted = await gateway.GetValuesAsync("ss-1", quotedRange, CancellationToken.None);
        var raw = await gateway.GetValuesAsync("ss-1", "My Sheet!A1:B1", CancellationToken.None);

        Assert.Equal(["a", "b"], quoted.Single());
        Assert.Equal(["a", "b"], raw.Single());
    }
}
