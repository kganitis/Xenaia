using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.Tenancy;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Bookings;
using Xenaia.Modules.Sync.Catalog;
using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;
using Xenaia.PortContracts.Fakes;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests;

/// <summary>
/// Milestone 2: the whole Sync loop end to end against the reference in-memory
/// booking system and spreadsheet gateway. One scenario walks catalog sync,
/// inbound booking sweep, outbound booking create, availability patch (REST and
/// patch-sheet), sheet write-back, and cancel, sharing the same stores across
/// flows the way the composed host does. Only the booking system, the
/// spreadsheet gateway, and the clock are test doubles; every module service is
/// the real one. No DB, no HTTP. Delayers are no-ops throughout: the production
/// default delays against the injected clock, and a FakeTimeProvider never
/// advances on its own, so a real delayer would block the run forever.
/// </summary>
public class MilestoneTwoEndToEndTests
{
    private const int ProductExternalId = 100;
    private const int OptionExternalId = 7;
    private const string SpreadsheetId = "ss-1";
    private const string GetSheet = "GetSheet";
    private const string PatchStatusRange = "PatchSheet!B2";
    private static readonly DateOnly Day = new(2026, 9, 5);
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ActivityAt = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly FakeTimeProvider _clock = new(FixedNow);
    private readonly CodeFormats _formats = new(Options.Create(new BookingsFormatOptions
    {
        BookingCodePattern = "^MT-[0-9]{4,}$",
        ProductCodePattern = "^MTP-[A-Z0-9]{4}$",
    }));

    [Fact]
    public async Task Full_sync_loop_catalog_inbound_outbound_availability_and_cancel()
    {
        // Shared collaborators: one booking system, one spreadsheet gateway,
        // and the same catalog/booking stores every flow reads and writes,
        // exactly as the composed host wires them.
        var provider = new InMemoryBookingSystemProvider();
        var gateway = new InMemorySpreadsheetGateway();
        var catalogStore = new FakeCatalogStore();
        var bookingStore = new FakeBookingStore();

        // Step 1: seed the vendor with a product (option + two participant
        // types) and two existing bookings updated inside the backfill window.
        provider.SeedProduct(
            new ProductSnapshot(ProductExternalId, "Sunset Kayak Tour", 3),
            new ProductOptionSnapshot(OptionExternalId, "Two-seat kayak",
                [new ParticipantTypeSnapshot("adult", "Adult"), new ParticipantTypeSnapshot("child", "Child")]));
        provider.SeedBooking(InboundSnapshot("MT-1001"));
        provider.SeedBooking(InboundSnapshot("MT-1002"));

        // Step 2: catalog sync lands the product, option, and participant
        // types in the catalog store, all Synced.
        var catalogSummary = await CatalogService(provider, catalogStore).RefreshAsync(CancellationToken.None);
        Assert.Equal(
            new CatalogSyncSummary(ProductsSeen: 1, ProductsAdded: 1, OptionsAdded: 1, ParticipantTypesAdded: 2),
            catalogSummary);
        var product = Assert.Single(catalogStore.Products);
        Assert.Equal(ProductExternalId, product.ExternalId);
        Assert.Equal(SyncStatus.Synced, product.Sync.Status);
        Assert.Equal(OptionExternalId, Assert.Single(product.Options).ExternalId);
        var types = await catalogStore.GetParticipantTypesAsync(ProductExternalId, OptionExternalId, CancellationToken.None);
        Assert.Equal(["adult", "child"], types.Select(t => t.Alias).OrderBy(a => a));

        // Step 3: inbound sweep ingests both vendor bookings, Synced, Inbound.
        var ingested = await InboundSweep(provider, bookingStore).RunAsync(CancellationToken.None);
        Assert.Equal(2, ingested);
        foreach (var code in new[] { "MT-1001", "MT-1002" })
        {
            var incoming = await bookingStore.GetByCodeAsync(code, CancellationToken.None);
            Assert.NotNull(incoming);
            Assert.Equal(SyncDirection.Inbound, incoming!.Direction);
            Assert.Equal(SyncStatus.Synced, incoming.Sync.Status);
        }

        // Step 4: enqueue an outbound create draft, run the pusher once. The
        // vendor receives the draft and assigns the code; the confirmed
        // snapshot is ingested back Outbound and the request is Synced.
        var bookingChannel = new BookingChannel(100);
        var requestStore = new FakeOutboundBookingRequestStore();
        var enqueuer = new OutboundBookingEnqueuer(
            requestStore, bookingStore, catalogStore, bookingChannel,
            NullLogger<OutboundBookingEnqueuer>.Instance);
        var draft = new BookingDraft
        {
            Type = BookingType.Api,
            LeadContactName = "Ada Coastline",
            Items = [new BookingDraftItem(ProductExternalId, OptionExternalId, "adult", ActivityAt, 49.50m)],
        };
        var createId = await enqueuer.EnqueueCreateAsync(draft, CancellationToken.None);
        var createOutcome = await BookingPusherFor(requestStore, provider, bookingStore, bookingChannel)
            .ProcessAsync(createId, CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, createOutcome);
        var createRequest = await requestStore.GetByIdAsync(createId, CancellationToken.None);
        Assert.Equal(SyncStatus.Synced, createRequest!.Sync.Status);
        var received = Assert.Single(provider.ReceivedCreates);
        Assert.Equal(ProductExternalId, received.Items.Single().ProductExternalId);
        const string createdCode = "MT-1000"; // InMemory provider's first assigned code
        var created = await bookingStore.GetByCodeAsync(createdCode, CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal(SyncDirection.Outbound, created!.Direction);
        Assert.Equal(SyncStatus.Synced, created.Sync.Status);

        // Step 5: two availability timeslots. The first goes through the
        // patch-sheet flow (a sheet context and a canonical A:E get-sheet row);
        // the second is a plain REST patch with no sheet. Run the pusher over
        // both queued items, then flush the buffered sheet write-backs.
        var availabilityStore = new FakeAvailabilityStore();
        var availabilityChannel = new AvailabilityChannel(100);
        var buffer = new SheetWriteBuffer();
        var patchService = new AvailabilityPatchService(
            availabilityStore, availabilityChannel, Options.Create(new SyncOptions()),
            NullLogger<AvailabilityPatchService>.Instance);

        await SeedGetSheet(gateway,
            ["09:00", "100", "7", "adult", $"100|7|{Day:yyyy-MM-dd}|{Day:yyyy-MM-dd}"]);

        var sheetItem = new AvailabilityPatchItem(
            ProductExternalId, OptionExternalId, Day, Day, [new TimeOnly(9, 0)], 5, false, PatchStatusRange);
        var restItem = new AvailabilityPatchItem(
            ProductExternalId, OptionExternalId, Day, Day, [new TimeOnly(14, 0)], 8, false, null);
        Assert.Equal(1, (await patchService.EnqueueAsync([sheetItem], SpreadsheetId, false, CancellationToken.None)).Accepted);
        Assert.Equal(1, (await patchService.EnqueueAsync([restItem], null, false, CancellationToken.None)).Accepted);

        var pusher = AvailabilityPusherFor(availabilityStore, provider, catalogStore, availabilityChannel, buffer, gateway);
        while (availabilityChannel.Reader.TryRead(out var item))
        {
            var outcome = await pusher.ProcessAsync(item, CancellationToken.None);
            Assert.Equal(PushOutcome.Synced, outcome);
        }
        await buffer.FlushAsync(gateway, GetSheet, CancellationToken.None);

        Assert.Equal(2, provider.ReceivedAvailabilityUpdates.Count);
        Assert.All(provider.ReceivedAvailabilityUpdates, u => Assert.Equal(["adult", "child"], u.ParticipantTypeAliases));
        var patched = await gateway.GetValuesAsync(SpreadsheetId, "PatchSheet!B2:B2", CancellationToken.None);
        Assert.StartsWith("Synced at ", Assert.Single(Assert.Single(patched)));
        var writeBack = await gateway.GetValuesAsync(SpreadsheetId, $"{GetSheet}!F1:H1", CancellationToken.None);
        Assert.Equal("5", Assert.Single(writeBack)[0]); // F1: vacancies written back

        // Step 6: cancel the created booking through the enqueuer and pusher.
        // The vendor sees the cancel and the local aggregate goes Cancelled.
        var cancelId = await enqueuer.EnqueueCancelAsync(createdCode, CancellationToken.None);
        var cancelOutcome = await BookingPusherFor(requestStore, provider, bookingStore, bookingChannel)
            .ProcessAsync(cancelId, CancellationToken.None);

        Assert.Equal(PushOutcome.Synced, cancelOutcome);
        Assert.Contains(createdCode, provider.ReceivedCancels);
        var cancelled = await bookingStore.GetByCodeAsync(createdCode, CancellationToken.None);
        Assert.Equal(BookingStatus.Cancelled, cancelled!.Status);
    }

    private BookingSnapshot InboundSnapshot(string code) => new()
    {
        Code = code,
        SecretCode = $"SEC-{code}",
        Type = BookingType.Api,
        Status = BookingStatus.Pending,
        FinalPrice = 100m,
        LeadContactName = "Alex Doe",
        Email = "alex.doe@example.com",
        CreatedAtExternal = FixedNow.AddDays(-1),
        UpdatedAtExternal = FixedNow.AddDays(-1),
    };

    private CatalogSyncService CatalogService(InMemoryBookingSystemProvider provider, FakeCatalogStore catalogStore) =>
        new(provider, catalogStore, CreateParticipantTypeCache(catalogStore),
            Options.Create(new SyncOptions()), _clock, NullLogger<CatalogSyncService>.Instance,
            (_, _) => Task.CompletedTask);

    private BookingInboundSweep InboundSweep(InMemoryBookingSystemProvider provider, FakeBookingStore bookingStore) =>
        new(provider, new BookingIngestService(bookingStore, _formats, _clock),
            new FakeSyncCheckpointStore(),
            Options.Create(new SyncOptions { Bookings = new BookingsOptions { BackfillDays = 30, OverlapSeconds = 60 } }),
            _clock, NullLogger<BookingInboundSweep>.Instance);

    private BookingPusher BookingPusherFor(
        FakeOutboundBookingRequestStore requestStore, InMemoryBookingSystemProvider provider,
        FakeBookingStore bookingStore, BookingChannel channel) =>
        new(requestStore, provider, new BookingIngestService(bookingStore, _formats, _clock),
            bookingStore, channel, new FakeNotificationService(), Options.Create(new SyncOptions()),
            _clock, NullLogger<BookingPusher>.Instance, (_, _) => Task.CompletedTask);

    private AvailabilityPusher AvailabilityPusherFor(
        FakeAvailabilityStore store, InMemoryBookingSystemProvider provider, FakeCatalogStore catalogStore,
        AvailabilityChannel channel, SheetWriteBuffer buffer, InMemorySpreadsheetGateway gateway)
    {
        var tenant = Options.Create(new TenantProfileOptions
        {
            BusinessName = "Meridian Trails", TimeZone = "Europe/Dublin", Locales = ["en-IE"],
        });
        return new AvailabilityPusher(
            store, provider, CreateParticipantTypeCache(catalogStore), channel, buffer,
            Options.Create(new SyncOptions()), tenant, _clock, NullLogger<AvailabilityPusher>.Instance,
            gateway, (_, _) => Task.CompletedTask);
    }

    private static ParticipantTypeCache CreateParticipantTypeCache(FakeCatalogStore catalogStore)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICatalogStore>(catalogStore)
            .BuildServiceProvider();
        return new ParticipantTypeCache(services.GetRequiredService<IServiceScopeFactory>());
    }

    private static Task SeedGetSheet(InMemorySpreadsheetGateway gateway, params string[][] rows) =>
        gateway.BatchUpdateAsync(
            SpreadsheetId,
            [new SheetValueRange($"{GetSheet}!A1:E{rows.Length}", rows.Select(r => (IReadOnlyList<string>)r).ToList())],
            CancellationToken.None);
}
