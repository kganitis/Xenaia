using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.Tenancy;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests.Availability;

public class AvailabilityFetchServiceTests
{
    private const string SpreadsheetId = "ss-1";
    private const string GetSheet = "GetSheet";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Two_combinations_fetch_upsert_synced_rows_and_write_vacancies_and_timestamp_back()
    {
        var store = new FakeAvailabilityStore();
        var provider = new InMemoryBookingSystemProvider();
        provider.SeedAvailability(100, 7,
            Slot(new DateOnly(2026, 8, 1), new TimeOnly(9, 0), 5),
            Slot(new DateOnly(2026, 8, 1), new TimeOnly(14, 0), 8));
        provider.SeedAvailability(200, 3, Slot(new DateOnly(2026, 8, 2), new TimeOnly(10, 0), 12));

        var gateway = new InMemorySpreadsheetGateway();
        await SeedSheet(gateway,
            ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
            ["14:00", "", "", "", ""],
            ["10:00", "200", "3", "adult", "200|3|2026-08-02|2026-08-02"]);

        var service = CreateService(store, provider, gateway);

        var summary = await service.SyncFromSheetAsync(SpreadsheetId, CancellationToken.None);

        Assert.Equal(3, summary.TotalRows);
        Assert.Equal(2, summary.Combinations);
        Assert.Equal(2, summary.SuccessfulFetches);
        Assert.Equal(0, summary.FailedFetches);
        Assert.Equal(3, summary.RowsUpdated);
        Assert.Equal(0, summary.TimeslotsNotFound);

        Assert.Equal(3, store.All.Count);
        Assert.All(store.All, row => Assert.Equal(SyncStatus.Synced, row.Sync.Status));

        var timestamp = ExpectedTimestamp();
        await AssertRow(gateway, 1, "5", timestamp);
        await AssertRow(gateway, 2, "8", timestamp);
        await AssertRow(gateway, 3, "12", timestamp);
    }

    [Fact]
    public async Task Unknown_product_writes_fetch_failed_and_counts_the_failure()
    {
        var store = new FakeAvailabilityStore();
        var provider = new InMemoryBookingSystemProvider(); // nothing seeded: product/option unknown
        var gateway = new InMemorySpreadsheetGateway();
        await SeedSheet(gateway, ["09:00", "999", "1", "adult", "999|1|2026-08-01|2026-08-01"]);

        var service = CreateService(store, provider, gateway);

        var summary = await service.SyncFromSheetAsync(SpreadsheetId, CancellationToken.None);

        Assert.Equal(1, summary.Combinations);
        Assert.Equal(0, summary.SuccessfulFetches);
        Assert.Equal(1, summary.FailedFetches);
        Assert.Equal(0, summary.RowsUpdated);
        Assert.Equal(0, summary.TimeslotsNotFound);
        Assert.Empty(store.All);

        await AssertRow(gateway, 1, "Fetch failed", ExpectedTimestamp());
    }

    [Fact]
    public async Task Time_absent_from_the_vendor_response_writes_time_not_found_and_counts_it()
    {
        var store = new FakeAvailabilityStore();
        var provider = new InMemoryBookingSystemProvider();
        // The vendor only knows about 14:00; the sheet declares 09:00.
        provider.SeedAvailability(100, 7, Slot(new DateOnly(2026, 8, 1), new TimeOnly(14, 0), 8));

        var gateway = new InMemorySpreadsheetGateway();
        await SeedSheet(gateway, ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"]);

        var service = CreateService(store, provider, gateway);

        var summary = await service.SyncFromSheetAsync(SpreadsheetId, CancellationToken.None);

        Assert.Equal(1, summary.SuccessfulFetches); // the vendor call itself succeeded
        Assert.Equal(0, summary.FailedFetches);
        Assert.Equal(0, summary.RowsUpdated);
        Assert.Equal(1, summary.TimeslotsNotFound);
        Assert.Empty(store.All);

        await AssertRow(gateway, 1, "Time not found", ExpectedTimestamp());
    }

    [Fact]
    public async Task Stop_sales_preserves_existing_vacancies_when_the_vendor_reports_zero()
    {
        var store = new FakeAvailabilityStore();
        var existing = AvailabilityAggregate.ForTimeslot(100, 7, new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero));
        existing.SetVacancies(5);
        existing.SetStopSales(true);
        existing.ClaimForSync();
        existing.MarkSynced(FixedNow.AddDays(-1));
        store.Seed(existing);

        var provider = new InMemoryBookingSystemProvider();
        provider.SeedAvailability(100, 7, Slot(new DateOnly(2026, 8, 1), new TimeOnly(9, 0), 0));

        var gateway = new InMemorySpreadsheetGateway();
        await SeedSheet(gateway, ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"]);

        var service = CreateService(store, provider, gateway);

        var summary = await service.SyncFromSheetAsync(SpreadsheetId, CancellationToken.None);

        Assert.Equal(1, summary.RowsUpdated);
        Assert.Equal(0, summary.TimeslotsNotFound);
        Assert.Equal(5, existing.Vacancies); // preserved, not zeroed
        Assert.True(existing.StopSales);
        Assert.Equal(SyncStatus.Synced, existing.Sync.Status);

        await AssertRow(gateway, 1, "5", ExpectedTimestamp());
    }

    [Fact]
    public async Task Fetch_delay_is_honored_between_combinations_via_the_injected_delayer()
    {
        var store = new FakeAvailabilityStore();
        var provider = new InMemoryBookingSystemProvider(); // both unknown: irrelevant to the delay assertion
        var gateway = new InMemorySpreadsheetGateway();
        await SeedSheet(gateway,
            ["09:00", "100", "7", "adult", "100|7|2026-08-01|2026-08-01"],
            ["09:00", "200", "3", "adult", "200|3|2026-08-02|2026-08-02"]);

        var delays = new List<TimeSpan>();
        var options = new SyncOptions { Availability = new AvailabilityOptions { GetSheetName = GetSheet, FetchDelayMs = 1234 } };
        var service = CreateService(
            store, provider, gateway, options,
            delayer: (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        await service.SyncFromSheetAsync(SpreadsheetId, CancellationToken.None);

        var delay = Assert.Single(delays); // one delay between the two combinations, none before the first
        Assert.Equal(TimeSpan.FromMilliseconds(1234), delay);
    }

    private static AvailabilityTimeslot Slot(DateOnly date, TimeOnly time, int vacancies) =>
        new(new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero), vacancies);

    private static async Task SeedSheet(InMemorySpreadsheetGateway gateway, params string[][] rows)
    {
        await gateway.BatchUpdateAsync(
            SpreadsheetId,
            [new SheetValueRange($"{GetSheet}!A1:E{rows.Length}", rows.Select(r => (IReadOnlyList<string>)r).ToList())],
            CancellationToken.None);
    }

    private static async Task AssertRow(
        InMemorySpreadsheetGateway gateway, int row, string expectedVacanciesCell, string expectedTimestamp)
    {
        var values = await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F{row}:G{row}", CancellationToken.None);
        Assert.Equal([expectedVacanciesCell, expectedTimestamp], Assert.Single(values));
    }

    private static string ExpectedTimestamp()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        var local = TimeZoneInfo.ConvertTime(FixedNow, zone);
        return local.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
    }

    private static AvailabilityFetchService CreateService(
        FakeAvailabilityStore store,
        IBookingSystemProvider provider,
        InMemorySpreadsheetGateway gateway,
        SyncOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        var opts = options ?? new SyncOptions { Availability = new AvailabilityOptions { GetSheetName = GetSheet } };
        var tenant = Options.Create(new TenantProfileOptions
        {
            BusinessName = "Meridian Trails", TimeZone = "Europe/Dublin", Locales = ["en-IE"],
        });
        var clock = new FakeTimeProvider(FixedNow);
        // Tests default to a no-op delayer: the service's production default
        // (Task.Delay against the injected clock) would block forever here
        // since FakeTimeProvider never advances on its own. Only the delay
        // test overrides this with its own recording delayer.
        return new AvailabilityFetchService(
            store, provider, gateway, new SheetCombinationParser(), Options.Create(opts), tenant, clock,
            NullLogger<AvailabilityFetchService>.Instance, delayer ?? ((_, _) => Task.CompletedTask));
    }
}
