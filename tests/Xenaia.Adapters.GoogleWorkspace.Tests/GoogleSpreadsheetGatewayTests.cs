using System.Globalization;
using System.Net;
using Google.Apis.Sheets.v4.Data;
using Xenaia.Modules.Sync.Spreadsheets;
using Xunit;

namespace Xenaia.Adapters.GoogleWorkspace.Tests;

/// <summary>
/// Unit tests for the testable logic in <see cref="GoogleSpreadsheetGateway"/>
/// against a fake <see cref="ISheetsApi"/>: cell mapping, batch chunking,
/// the 429 retry policy, and EnsureTabAsync idempotency (spec section 9).
/// </summary>
public class GoogleSpreadsheetGatewayTests
{
    private const string SpreadsheetId = "MT-SHEET-1";

    [Fact]
    public async Task GetValuesAsync_maps_object_cells_to_strings_with_null_cells_as_empty_string()
    {
        var api = new FakeSheetsApi
        {
            Values = new List<IList<object>>
            {
                Row("alpha", null, 42),
                Row(null, "beta"),
            },
        };
        var sut = new GoogleSpreadsheetGateway(api);

        var values = await sut.GetValuesAsync(SpreadsheetId, "Availability!A1:C2", CancellationToken.None);

        Assert.Equal(2, values.Count);
        Assert.Equal(["alpha", "", "42"], values[0]);
        Assert.Equal(["", "beta"], values[1]);
    }

    [Fact]
    public async Task GetValuesAsync_maps_numeric_cells_with_invariant_culture_regardless_of_the_current_culture()
    {
        var api = new FakeSheetsApi
        {
            Values = new List<IList<object>> { Row(3.5) },
        };
        var sut = new GoogleSpreadsheetGateway(api);

        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma-decimal locale
        try
        {
            var values = await sut.GetValuesAsync(SpreadsheetId, "Availability!A1", CancellationToken.None);

            Assert.Equal(["3.5"], values[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task GetValuesAsync_returns_an_empty_list_when_the_api_returns_no_values()
    {
        var api = new FakeSheetsApi { Values = null };
        var sut = new GoogleSpreadsheetGateway(api);

        var values = await sut.GetValuesAsync(SpreadsheetId, "Availability!A1:C2", CancellationToken.None);

        Assert.Empty(values);
    }

    [Fact]
    public async Task BatchUpdateAsync_chunks_writes_at_100_ranges_per_request()
    {
        var api = new FakeSheetsApi();
        var sut = new GoogleSpreadsheetGateway(api);
        var updates = Enumerable.Range(0, 250)
            .Select(i => new SheetValueRange(
                $"Availability!A{i + 1}", [[i.ToString(CultureInfo.InvariantCulture)]]))
            .ToList();

        await sut.BatchUpdateAsync(SpreadsheetId, updates, CancellationToken.None);

        Assert.Equal(3, api.BatchUpdateCalls.Count);
        Assert.Equal(100, api.BatchUpdateCalls[0].Data.Count);
        Assert.Equal(100, api.BatchUpdateCalls[1].Data.Count);
        Assert.Equal(50, api.BatchUpdateCalls[2].Data.Count);
        Assert.All(api.BatchUpdateCalls, call => Assert.Equal(SpreadsheetId, call.SpreadsheetId));
    }

    [Fact]
    public async Task BatchUpdateAsync_retries_a_429_twice_with_a_60_second_delay_then_succeeds()
    {
        var api = new FakeSheetsApi();
        api.BatchUpdateExceptions.Enqueue(TooManyRequests());
        api.BatchUpdateExceptions.Enqueue(TooManyRequests());
        var delays = new List<TimeSpan>();
        var sut = new GoogleSpreadsheetGateway(api, Delayer(delays));

        await sut.BatchUpdateAsync(
            SpreadsheetId, [new SheetValueRange("Availability!A1", [["1"]])], CancellationToken.None);

        Assert.Equal(3, api.BatchUpdateCallCount);
        Assert.Single(api.BatchUpdateCalls);
        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)], delays);
    }

    [Fact]
    public async Task BatchUpdateAsync_propagates_after_a_third_429()
    {
        var api = new FakeSheetsApi();
        api.BatchUpdateExceptions.Enqueue(TooManyRequests());
        api.BatchUpdateExceptions.Enqueue(TooManyRequests());
        api.BatchUpdateExceptions.Enqueue(TooManyRequests());
        var delays = new List<TimeSpan>();
        var sut = new GoogleSpreadsheetGateway(api, Delayer(delays));

        await Assert.ThrowsAsync<Google.GoogleApiException>(() => sut.BatchUpdateAsync(
            SpreadsheetId, [new SheetValueRange("Availability!A1", [["1"]])], CancellationToken.None));

        Assert.Equal(3, api.BatchUpdateCallCount);
        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)], delays);
    }

    [Fact]
    public async Task BatchUpdateAsync_propagates_a_non_429_error_immediately_without_retrying()
    {
        var api = new FakeSheetsApi();
        api.BatchUpdateExceptions.Enqueue(OtherError());
        var delays = new List<TimeSpan>();
        var sut = new GoogleSpreadsheetGateway(api, Delayer(delays));

        await Assert.ThrowsAsync<Google.GoogleApiException>(() => sut.BatchUpdateAsync(
            SpreadsheetId, [new SheetValueRange("Availability!A1", [["1"]])], CancellationToken.None));

        Assert.Equal(1, api.BatchUpdateCallCount);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task EnsureTabAsync_adds_the_tab_when_it_is_missing()
    {
        var api = new FakeSheetsApi { Titles = ["Availability"] };
        var sut = new GoogleSpreadsheetGateway(api);

        await sut.EnsureTabAsync(SpreadsheetId, "Bookings", CancellationToken.None);

        Assert.Equal(["Bookings"], api.AddSheetTitles);
    }

    [Fact]
    public async Task EnsureTabAsync_is_a_no_op_when_the_tab_is_already_present()
    {
        var api = new FakeSheetsApi { Titles = ["Availability", "Bookings"] };
        var sut = new GoogleSpreadsheetGateway(api);

        await sut.EnsureTabAsync(SpreadsheetId, "Bookings", CancellationToken.None);

        Assert.Equal(0, api.AddSheetCallCount);
        Assert.Empty(api.AddSheetTitles);
    }

    [Fact]
    public async Task EnsureTabAsync_swallows_an_already_exists_race()
    {
        var api = new FakeSheetsApi { Titles = [] };
        api.AddSheetExceptions.Enqueue(AlreadyExists("Bookings"));
        var sut = new GoogleSpreadsheetGateway(api);

        await sut.EnsureTabAsync(SpreadsheetId, "Bookings", CancellationToken.None);

        Assert.Equal(1, api.AddSheetCallCount);
        Assert.Empty(api.AddSheetTitles); // the throwing call never recorded a title
    }

    [Fact]
    public async Task EnsureTabAsync_does_not_swallow_an_unrelated_add_failure()
    {
        var api = new FakeSheetsApi { Titles = [] };
        api.AddSheetExceptions.Enqueue(OtherError());
        var sut = new GoogleSpreadsheetGateway(api);

        await Assert.ThrowsAsync<Google.GoogleApiException>(() =>
            sut.EnsureTabAsync(SpreadsheetId, "Bookings", CancellationToken.None));
    }

    private static IList<object> Row(params object?[] cells) => cells.Select(c => c!).ToList();

    private static Func<TimeSpan, CancellationToken, Task> Delayer(List<TimeSpan> sink)
        => (delay, _) => { sink.Add(delay); return Task.CompletedTask; };

    private static Google.GoogleApiException TooManyRequests() =>
        new("sheets", "Rate limit exceeded") { HttpStatusCode = (HttpStatusCode)429 };

    private static Google.GoogleApiException OtherError() =>
        new("sheets", "Bad request") { HttpStatusCode = HttpStatusCode.BadRequest };

    private static Google.GoogleApiException AlreadyExists(string title) =>
        new("sheets", $"A sheet with the name \"{title}\" already exists. Please enter another name.")
        {
            HttpStatusCode = HttpStatusCode.BadRequest,
        };
}
