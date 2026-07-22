using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>
/// Bookings inbound sweep (spec 6.3): reads the "bookings-inbound" checkpoint,
/// queries the booking system for everything updated since checkpoint minus
/// overlap (or, on first run with no checkpoint, since BackfillDays back from
/// the sweep's start), ingests each snapshot through
/// <see cref="BookingIngestService"/>, then advances the checkpoint to the
/// sweep's start instant (not "now" at the end, so a booking updated while
/// the sweep is mid-flight is never missed). A snapshot that fails to ingest
/// (e.g. a malformed code, or an aggregate-invariant violation) is logged and
/// skipped, not fatal: the checkpoint still advances, and since the failing
/// row's own UpdatedAtExternal has not moved, the next sweep's overlap window
/// picks it up again. Permanently bad data must not wedge the sweep. If the
/// provider call itself fails, the whole sweep is logged and abandoned
/// without advancing the checkpoint, so the same window is retried next
/// cycle. Scoped: a fresh instance per <see cref="BookingPollingService"/>
/// tick.
/// </summary>
public sealed class BookingInboundSweep(
    IBookingSystemProvider provider,
    BookingIngestService ingestService,
    ISyncCheckpointStore checkpointStore,
    IOptions<SyncOptions> options,
    TimeProvider clock,
    ILogger<BookingInboundSweep> logger)
{
    public const string CheckpointName = "bookings-inbound";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var sweepStart = clock.GetUtcNow();
        var bookingsOptions = options.Value.Bookings;

        IReadOnlyList<BookingSnapshot> snapshots;
        try
        {
            var checkpoint = await checkpointStore.GetAsync(CheckpointName, ct);
            var updatedFrom = checkpoint is { } lastCheckpoint
                ? lastCheckpoint - TimeSpan.FromSeconds(bookingsOptions.OverlapSeconds)
                : sweepStart - TimeSpan.FromDays(bookingsOptions.BackfillDays);

            snapshots = await provider.GetBookingsAsync(new BookingQuery { UpdatedFrom = updatedFrom }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bookings inbound sweep failed to fetch bookings; checkpoint left unchanged for retry");
            return 0;
        }

        var ingested = 0;
        foreach (var snapshot in snapshots)
        {
            try
            {
                await ingestService.UpsertFromSnapshotAsync(snapshot, SyncDirection.Inbound, ct);
                ingested++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "Bookings inbound sweep failed to ingest booking {Code}; will retry via next sweep's overlap",
                    snapshot.Code);
            }
        }

        await checkpointStore.SetAsync(CheckpointName, sweepStart, ct);
        return ingested;
    }
}
