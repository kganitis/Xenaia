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

public class AvailabilityPusherTests
{
    private const int ProductId = 100;
    private const int OptionId = 7;
    private static readonly DateOnly Day = new(2026, 8, 1);
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 1, 12, 30, 0, TimeSpan.Zero);

    // Group 2: happy path.
    [Fact]
    public async Task Happy_path_claims_pushes_the_right_update_marks_synced_and_buffers_sheet_writes()
    {
        var store = new FakeAvailabilityStore();
        var row = SeedPending(store, new TimeOnly(9, 0), vacancies: 5, stopSales: false);
        var provider = new InMemoryBookingSystemProvider();
        var catalog = new FakeCatalogStore();
        catalog.Seed(ProductId, OptionId, "adult", "child");
        var buffer = new SheetWriteBuffer();
        var pusher = CreatePusher(store, provider, catalog, buffer, new InMemorySpreadsheetGateway());

        var item = new AvailabilityWorkItem(row.Id, new SheetWriteContext("ss-1", "PatchSheet!B5", null));
        var outcome = await pusher.ProcessAsync(item, CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, outcome);
        Assert.Equal(SyncStatus.Synced, row.Sync.Status);

        var update = Assert.Single(provider.ReceivedAvailabilityUpdates);
        Assert.Equal(ProductId, update.ProductExternalId);
        Assert.Equal(OptionId, update.OptionExternalId);
        Assert.Equal(new DateTimeOffset(Day.ToDateTime(new TimeOnly(0, 1)), TimeSpan.Zero), update.From);
        Assert.Equal(new DateTimeOffset(Day.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero), update.To);
        Assert.Equal([new TimeOnly(9, 0)], update.Times);
        Assert.Equal(5, update.Vacancies);
        Assert.False(update.StopSales);
        Assert.Equal(["adult", "child"], update.ParticipantTypeAliases);

        var patch = Assert.Single(buffer.PatchStatusWrites);
        Assert.Equal("ss-1", patch.SpreadsheetId);
        Assert.Equal("PatchSheet!B5", patch.Range);
        Assert.StartsWith("Synced at ", patch.Value);

        var getRow = Assert.Single(buffer.GetRowWrites);
        Assert.Equal("ss-1", getRow.SpreadsheetId);
        Assert.Equal(new SheetWriteBuffer.GetRowKey(ProductId, OptionId, row.TimeslotAt), getRow.Key);
        Assert.Equal(5, getRow.Vacancies);
        Assert.False(getRow.StopSales);
    }

    [Fact]
    public async Task Slotless_row_omits_times_from_the_update()
    {
        var store = new FakeAvailabilityStore();
        var row = SeedPending(store, TimeOnly.MinValue, vacancies: 3); // 00:00 sentinel
        var provider = new InMemoryBookingSystemProvider();
        var pusher = CreatePusher(store, provider, new FakeCatalogStore(), new SheetWriteBuffer(), gateway: null);

        var outcome = await pusher.ProcessAsync(new AvailabilityWorkItem(row.Id, null), CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, outcome);
        var update = Assert.Single(provider.ReceivedAvailabilityUpdates);
        Assert.Null(update.Times);
    }

    // Group 3: vendor failure after retries.
    [Fact]
    public async Task Vendor_failure_marks_failed_truncates_the_error_and_buffers_the_error_cell()
    {
        var store = new FakeAvailabilityStore();
        var row = SeedPending(store, new TimeOnly(9, 0), vacancies: 5);
        var longMessage = new string('x', 2500);
        var provider = new ThrowingBookingSystemProvider(new BookingSystemException(longMessage));
        var buffer = new SheetWriteBuffer();
        var delays = new List<TimeSpan>();
        var pusher = CreatePusher(
            store, provider, new FakeCatalogStore(), buffer, new InMemorySpreadsheetGateway(),
            delayer: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        var item = new AvailabilityWorkItem(row.Id, new SheetWriteContext("ss-1", "PatchSheet!B5", null));
        var outcome = await pusher.ProcessAsync(item, CancellationToken.None);

        Assert.Equal(PushOutcome.Failed, outcome);
        Assert.Equal(SyncStatus.Failed, row.Sync.Status);
        Assert.Equal(4, provider.UpdateCallCount);                 // retried to exhaustion
        Assert.Equal(3, delays.Count);                             // 3 backoffs between 4 tries
        Assert.Equal(2000, row.Sync.Error!.Length);               // truncated to 2000
        Assert.Equal(new string('x', 2000), row.Sync.Error);

        var patch = Assert.Single(buffer.PatchStatusWrites);
        Assert.Equal(new string('x', 2000), patch.Value);
        Assert.Empty(buffer.GetRowWrites);                         // no get write-back on failure
    }

    // Group 4: lost claim.
    [Fact]
    public async Task Lost_claim_returns_lost_claim_and_never_calls_the_vendor()
    {
        var store = new FakeAvailabilityStore();
        var row = SeedPending(store, new TimeOnly(9, 0), vacancies: 5);
        row.ClaimForSync(); // already Processing: the claim will fail
        var provider = new InMemoryBookingSystemProvider();
        var buffer = new SheetWriteBuffer();
        var pusher = CreatePusher(store, provider, new FakeCatalogStore(), buffer, new InMemorySpreadsheetGateway());

        var item = new AvailabilityWorkItem(row.Id, new SheetWriteContext("ss-1", "PatchSheet!B5", null));
        var outcome = await pusher.ProcessAsync(item, CancellationToken.None);

        Assert.Equal(PushOutcome.LostClaim, outcome);
        Assert.Empty(provider.ReceivedAvailabilityUpdates);
        Assert.True(buffer.IsEmpty);
        Assert.Equal(SyncStatus.Processing, row.Sync.Status); // untouched
    }

    // Group 5: recovery.
    [Fact]
    public async Task Recovery_resets_processing_and_re_enqueues_all_pending_with_no_sheet_context()
    {
        var store = new FakeAvailabilityStore();
        var stuckA = SeedPending(store, new TimeOnly(9, 0), vacancies: 5);
        var stuckB = SeedPending(store, new TimeOnly(10, 0), vacancies: 5);
        stuckA.ClaimForSync();
        stuckB.ClaimForSync(); // both Processing (stuck)
        var pending = SeedPending(store, new TimeOnly(11, 0), vacancies: 5); // already Pending

        var channel = new AvailabilityChannel(100);
        var pusher = CreatePusher(
            store, new InMemoryBookingSystemProvider(), new FakeCatalogStore(),
            new SheetWriteBuffer(), gateway: null, channel: channel);

        var enqueued = await pusher.RecoverAsync(CancellationToken.None);

        Assert.Equal(3, enqueued);
        Assert.Equal(SyncStatus.Pending, stuckA.Sync.Status);
        Assert.Equal(SyncStatus.Pending, stuckB.Sync.Status);
        Assert.Equal(SyncStatus.Pending, pending.Sync.Status);

        var items = Drain(channel);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Null(i.Sheet));
        Assert.Equal(
            new[] { stuckA.Id, stuckB.Id, pending.Id }.ToHashSet(),
            items.Select(i => i.AvailabilityId).ToHashSet());
    }

    // Group 6: no spreadsheet gateway registered.
    [Fact]
    public async Task With_no_gateway_the_outcome_is_unchanged_and_no_sheet_writes_are_buffered()
    {
        var store = new FakeAvailabilityStore();
        var row = SeedPending(store, new TimeOnly(9, 0), vacancies: 5);
        var provider = new InMemoryBookingSystemProvider();
        var buffer = new SheetWriteBuffer();
        var pusher = CreatePusher(store, provider, new FakeCatalogStore(), buffer, gateway: null);

        // Sheet context is present, but with no gateway it must be ignored.
        var item = new AvailabilityWorkItem(row.Id, new SheetWriteContext("ss-1", "PatchSheet!B5", null));
        var outcome = await pusher.ProcessAsync(item, CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, outcome);
        Assert.Equal(SyncStatus.Synced, row.Sync.Status);
        Assert.Single(provider.ReceivedAvailabilityUpdates);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public async Task Participant_types_are_cached_across_items_for_the_same_option()
    {
        var store = new FakeAvailabilityStore();
        var rowA = SeedPending(store, new TimeOnly(9, 0), vacancies: 5);
        var rowB = SeedPending(store, new TimeOnly(10, 0), vacancies: 6);
        var catalog = new FakeCatalogStore();
        catalog.Seed(ProductId, OptionId, "adult");
        var pusher = CreatePusher(
            store, new InMemoryBookingSystemProvider(), catalog, new SheetWriteBuffer(), gateway: null);

        await pusher.ProcessAsync(new AvailabilityWorkItem(rowA.Id, null), CancellationToken.None);
        await pusher.ProcessAsync(new AvailabilityWorkItem(rowB.Id, null), CancellationToken.None);

        Assert.Equal(1, catalog.GetParticipantTypesCallCount); // second item hit the cache
    }

    private static AvailabilityAggregate SeedPending(
        FakeAvailabilityStore store, TimeOnly time, int? vacancies = null, bool? stopSales = null)
    {
        var row = AvailabilityAggregate.ForTimeslot(
            ProductId, OptionId, new DateTimeOffset(Day.ToDateTime(time), TimeSpan.Zero));
        if (vacancies is not null)
            row.SetVacancies(vacancies.Value);
        if (stopSales is not null)
            row.SetStopSales(stopSales.Value);
        store.Seed(row);
        return row;
    }

    private static AvailabilityPusher CreatePusher(
        FakeAvailabilityStore store,
        IBookingSystemProvider provider,
        FakeCatalogStore catalog,
        SheetWriteBuffer buffer,
        ISpreadsheetGateway? gateway,
        AvailabilityChannel? channel = null,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        var options = Options.Create(new SyncOptions());
        var tenant = Options.Create(new TenantProfileOptions { BusinessName = "Meridian Trails", TimeZone = "Europe/Dublin", Locales = ["en-IE"] });
        var clock = new FakeTimeProvider(FixedNow);
        return new AvailabilityPusher(
            store, provider, catalog, channel ?? new AvailabilityChannel(100), buffer,
            options, tenant, clock, NullLogger<AvailabilityPusher>.Instance, gateway,
            delayer ?? ((_, _) => Task.CompletedTask));
    }

    private static List<AvailabilityWorkItem> Drain(AvailabilityChannel channel)
    {
        var items = new List<AvailabilityWorkItem>();
        while (channel.Reader.TryRead(out var item))
            items.Add(item);
        return items;
    }
}
