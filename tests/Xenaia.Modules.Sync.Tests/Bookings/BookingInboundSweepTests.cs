using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Bookings;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;

namespace Xenaia.Modules.Sync.Tests.Bookings;

public class BookingInboundSweepTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly CodeFormats _formats = new(Options.Create(new BookingsFormatOptions
    {
        BookingCodePattern = "^MT-[0-9]{4,}$",
        ProductCodePattern = "^MTP-[A-Z0-9]{4}$",
    }));

    [Fact]
    public async Task No_checkpoint_backfills_from_now_minus_backfill_days_and_advances_checkpoint_to_sweep_start()
    {
        var options = new SyncOptions { Bookings = new BookingsOptions { BackfillDays = 30, OverlapSeconds = 60 } };
        var cutoff = FixedNow - TimeSpan.FromDays(30);
        var provider = new InMemoryBookingSystemProvider();
        provider.SeedBooking(Snapshot("MT-1001", updatedAt: cutoff));               // exactly at cutoff: included
        provider.SeedBooking(Snapshot("MT-1002", updatedAt: cutoff - TimeSpan.FromSeconds(1))); // just before: excluded
        var bookingStore = new FakeBookingStore();
        var checkpointStore = new FakeSyncCheckpointStore();
        var sut = CreateSut(provider, bookingStore, checkpointStore, options);

        var ingested = await sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, ingested);
        Assert.NotNull(await bookingStore.GetByCodeAsync("MT-1001", CancellationToken.None));
        Assert.Null(await bookingStore.GetByCodeAsync("MT-1002", CancellationToken.None));

        var checkpoint = await checkpointStore.GetAsync(BookingInboundSweep.CheckpointName, CancellationToken.None);
        Assert.Equal(FixedNow, checkpoint);
    }

    [Fact]
    public async Task Checkpoint_present_queries_from_checkpoint_minus_overlap()
    {
        var options = new SyncOptions { Bookings = new BookingsOptions { BackfillDays = 30, OverlapSeconds = 60 } };
        var lastCheckpoint = FixedNow - TimeSpan.FromDays(1);
        var cutoff = lastCheckpoint - TimeSpan.FromSeconds(60);
        var provider = new InMemoryBookingSystemProvider();
        provider.SeedBooking(Snapshot("MT-2001", updatedAt: cutoff));               // exactly at cutoff: included
        provider.SeedBooking(Snapshot("MT-2002", updatedAt: cutoff - TimeSpan.FromSeconds(1))); // just before: excluded
        var bookingStore = new FakeBookingStore();
        var checkpointStore = new FakeSyncCheckpointStore();
        checkpointStore.Seed(BookingInboundSweep.CheckpointName, lastCheckpoint);
        var sut = CreateSut(provider, bookingStore, checkpointStore, options);

        var ingested = await sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, ingested);
        Assert.NotNull(await bookingStore.GetByCodeAsync("MT-2001", CancellationToken.None));
        Assert.Null(await bookingStore.GetByCodeAsync("MT-2002", CancellationToken.None));

        var checkpoint = await checkpointStore.GetAsync(BookingInboundSweep.CheckpointName, CancellationToken.None);
        Assert.Equal(FixedNow, checkpoint);
    }

    [Fact]
    public async Task One_bad_code_is_logged_and_skipped_but_others_ingest_and_checkpoint_still_advances()
    {
        var options = new SyncOptions { Bookings = new BookingsOptions { BackfillDays = 30, OverlapSeconds = 60 } };
        var provider = new InMemoryBookingSystemProvider();
        provider.SeedBooking(Snapshot("MT-3001", updatedAt: FixedNow.AddDays(-1)));
        provider.SeedBooking(Snapshot("BAD-CODE", updatedAt: FixedNow.AddDays(-1))); // fails BookingCode.Create
        var bookingStore = new FakeBookingStore();
        var checkpointStore = new FakeSyncCheckpointStore();
        var sut = CreateSut(provider, bookingStore, checkpointStore, options);

        var ingested = await sut.RunAsync(CancellationToken.None);

        Assert.Equal(1, ingested);
        Assert.NotNull(await bookingStore.GetByCodeAsync("MT-3001", CancellationToken.None));
        Assert.Null(await bookingStore.GetByCodeAsync("BAD-CODE", CancellationToken.None));

        var checkpoint = await checkpointStore.GetAsync(BookingInboundSweep.CheckpointName, CancellationToken.None);
        Assert.Equal(FixedNow, checkpoint);
    }

    [Fact]
    public async Task Provider_failure_is_logged_not_rethrown_and_checkpoint_is_not_advanced()
    {
        var options = new SyncOptions { Bookings = new BookingsOptions { BackfillDays = 30, OverlapSeconds = 60 } };
        var provider = new InMemoryBookingSystemProvider { FailNextCallWith = new InvalidOperationException("boom") };
        var bookingStore = new FakeBookingStore();
        var checkpointStore = new FakeSyncCheckpointStore();
        var priorCheckpoint = FixedNow - TimeSpan.FromDays(1);
        checkpointStore.Seed(BookingInboundSweep.CheckpointName, priorCheckpoint);
        var sut = CreateSut(provider, bookingStore, checkpointStore, options);

        var ingested = await sut.RunAsync(CancellationToken.None);

        Assert.Equal(0, ingested);
        Assert.Empty(bookingStore.Bookings);

        var checkpoint = await checkpointStore.GetAsync(BookingInboundSweep.CheckpointName, CancellationToken.None);
        Assert.Equal(priorCheckpoint, checkpoint);
    }

    private static BookingSnapshot Snapshot(string code, DateTimeOffset updatedAt) => new()
    {
        Code = code,
        SecretCode = $"SEC-{code}",
        Type = BookingType.Api,
        Status = BookingStatus.Pending,
        FinalPrice = 100m,
        LeadContactName = "Alex Doe",
        Email = "alex.doe@example.com",
        CreatedAtExternal = updatedAt,
        UpdatedAtExternal = updatedAt,
    };

    private BookingInboundSweep CreateSut(
        InMemoryBookingSystemProvider provider,
        FakeBookingStore bookingStore,
        FakeSyncCheckpointStore checkpointStore,
        SyncOptions options)
    {
        var clock = new FakeTimeProvider(FixedNow);
        var ingestService = new BookingIngestService(bookingStore, _formats, clock);
        return new BookingInboundSweep(
            provider, ingestService, checkpointStore, Options.Create(options), clock,
            NullLogger<BookingInboundSweep>.Instance);
    }
}
