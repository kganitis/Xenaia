using Xenaia.Modules.Sync.Spreadsheets;
using Xunit;

namespace Xenaia.PortContracts.Spreadsheets;

/// <summary>
/// Reusable behavioral contract for ISpreadsheetGateway. Any adapter must
/// pass this suite; inherit it, implement the harness hook, and the port's
/// semantics are asserted for free (design section 7/8, port contract tests).
/// </summary>
public abstract class SpreadsheetGatewayContractTests
{
    /// <summary>Creates a gateway with an empty backing store.</summary>
    protected abstract Task<ISpreadsheetGateway> CreateGatewayAsync();

    private const string SpreadsheetId = "MT-SHEET-1";

    [Fact]
    public async Task Reading_an_empty_tab_returns_an_empty_list()
    {
        var gateway = await CreateGatewayAsync();
        await gateway.EnsureTabAsync(SpreadsheetId, "Availability", CancellationToken.None);

        var values = await gateway.GetValuesAsync(SpreadsheetId, "Availability!A1:C3", CancellationToken.None);

        Assert.Empty(values);
    }

    [Fact]
    public async Task Reading_an_unknown_tab_returns_an_empty_list()
    {
        var gateway = await CreateGatewayAsync();

        var values = await gateway.GetValuesAsync(SpreadsheetId, "Missing!A:G", CancellationToken.None);

        Assert.Empty(values);
    }

    [Fact]
    public async Task Writing_then_reading_round_trips_the_values()
    {
        var gateway = await CreateGatewayAsync();

        await gateway.BatchUpdateAsync(SpreadsheetId,
            [new SheetValueRange("Availability!A1:B2", [["1", "2"], ["3", "4"]])],
            CancellationToken.None);

        var values = await gateway.GetValuesAsync(SpreadsheetId, "Availability!A1:B2", CancellationToken.None);

        Assert.Equal(2, values.Count);
        Assert.Equal(["1", "2"], values[0]);
        Assert.Equal(["3", "4"], values[1]);
    }

    [Fact]
    public async Task EnsureTabAsync_is_idempotent()
    {
        var gateway = await CreateGatewayAsync();

        await gateway.EnsureTabAsync(SpreadsheetId, "Bookings", CancellationToken.None);
        await gateway.EnsureTabAsync(SpreadsheetId, "Bookings", CancellationToken.None);

        var values = await gateway.GetValuesAsync(SpreadsheetId, "Bookings!A:G", CancellationToken.None);

        Assert.Empty(values);
    }

    [Fact]
    public async Task Batch_update_writes_multiple_ranges()
    {
        var gateway = await CreateGatewayAsync();

        await gateway.BatchUpdateAsync(SpreadsheetId,
        [
            new SheetValueRange("Availability!A1:A1", [["alpha"]]),
            new SheetValueRange("Bookings!A1:A1", [["beta"]]),
        ], CancellationToken.None);

        var availability = await gateway.GetValuesAsync(SpreadsheetId, "Availability!A1:A1", CancellationToken.None);
        var bookings = await gateway.GetValuesAsync(SpreadsheetId, "Bookings!A1:A1", CancellationToken.None);

        Assert.Equal(["alpha"], Assert.Single(availability));
        Assert.Equal(["beta"], Assert.Single(bookings));
    }
}
