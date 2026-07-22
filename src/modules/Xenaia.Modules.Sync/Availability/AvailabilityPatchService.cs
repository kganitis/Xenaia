using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
// Alias needed: this file's own namespace ends in "Availability", which
// shadows the bare aggregate type name Xenaia.Domain.Bookings.Availabilities.Availability.
using AvailabilityAggregate = Xenaia.Domain.Bookings.Availabilities.Availability;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>Outcome of one EnqueueAsync call: how many expanded rows were
/// merged and re-enqueued versus left untouched.</summary>
public sealed record AvailabilityPatchResult(int Accepted, int Skipped);

/// <summary>
/// Accepts patch-sheet/REST availability updates, expands each item's times
/// into per-timeslot rows, and dedups value-aware against the durable queue
/// (spec 6.1 step 2): a row in flight (Processing) is always skipped; a row
/// whose effective values would not change is skipped unless the caller
/// forces it; otherwise the incoming signals are merged onto the aggregate,
/// the row is requeued to Pending (only legal from Synced/Failed/Processing;
/// a Pending row just gets its setters called), and a work item is written to
/// the channel so the processor wakes up.
/// </summary>
public sealed class AvailabilityPatchService(
    IAvailabilityStore store, AvailabilityChannel channel,
    IOptions<SyncOptions> options, ILogger<AvailabilityPatchService> logger)
{
    private const int SaveBatchSize = 50;

    public async Task<AvailabilityPatchResult> EnqueueAsync(
        IReadOnlyList<AvailabilityPatchItem> items,
        string? spreadsheetId, bool force, CancellationToken ct)
    {
        var maxBatchSize = options.Value.Availability.MaxBatchSize;
        if (items.Count > maxBatchSize)
            throw new ArgumentException(
                $"Batch of {items.Count} items exceeds Sync:Availability:MaxBatchSize ({maxBatchSize}).",
                nameof(items));

        var expanded = new List<(AvailabilityKey Key, AvailabilityPatchItem Source)>();
        foreach (var item in items)
        {
            foreach (var timeslotAt in ExpandTimeslots(item))
                expanded.Add((new AvailabilityKey(item.ProductExternalId, item.OptionExternalId, timeslotAt), item));
        }

        // One GetByKeysAsync call for the whole batch: the DB round trip is
        // the expensive part, not the in-memory dedup that follows.
        var existingByKey = (await store.GetByKeysAsync(expanded.Select(e => e.Key).Distinct().ToList(), ct))
            .ToDictionary(a => new AvailabilityKey(a.ExternalProductId, a.ExternalOptionId, a.TimeslotAt));

        var accepted = 0;
        var skipped = 0;
        var pendingSaves = 0;

        // New rows have no real id until SaveChangesAsync assigns one (EF's
        // deferred key generation), so their work items are buffered here and
        // only written to the channel once the save that persisted them has
        // run. Existing rows already carry a real id and can be written as
        // they're accepted.
        var pendingNewRowWorkItems = new List<(AvailabilityAggregate Row, SheetWriteContext? Sheet)>();

        foreach (var (key, source) in expanded)
        {
            AvailabilityAggregate row;
            bool isNew;
            if (!existingByKey.TryGetValue(key, out var existing))
            {
                row = AvailabilityAggregate.ForTimeslot(key.ProductExternalId, key.OptionExternalId, key.TimeslotAt);
                await store.AddAsync(row, ct);
                existingByKey[key] = row;
                isNew = true;
            }
            else
            {
                row = existing;
                isNew = false;
                if (row.Sync.Status == SyncStatus.Processing)
                {
                    // In flight: never touch a row a worker currently owns, force or not.
                    skipped++;
                    continue;
                }
                if (!ValuesWouldChange(row, source.Vacancies, source.StopSales) && !force)
                {
                    skipped++;
                    continue;
                }
            }

            if (source.Vacancies is not null)
                row.SetVacancies(source.Vacancies.Value);
            if (source.StopSales is not null)
                row.SetStopSales(source.StopSales.Value);

            // Requeue is only a legal transition from Synced/Failed/Processing;
            // a Pending row (new or unclaimed) keeps its state as-is.
            if (row.Sync.Status != SyncStatus.Pending)
                row.RequeueSync();

            accepted++;

            var sheet = spreadsheetId is not null
                ? new SheetWriteContext(spreadsheetId, source.PatchStatusRange, GetRowRange: null)
                : null;

            if (isNew)
                pendingNewRowWorkItems.Add((row, sheet));
            else
                await channel.Writer.WriteAsync(new AvailabilityWorkItem(row.Id, sheet), ct);

            pendingSaves++;
            if (pendingSaves >= SaveBatchSize)
            {
                await store.SaveChangesAsync(ct);
                await FlushNewRowWorkItemsAsync(pendingNewRowWorkItems, channel, ct);
                pendingSaves = 0;
            }
        }

        if (pendingSaves > 0)
            await store.SaveChangesAsync(ct);
        await FlushNewRowWorkItemsAsync(pendingNewRowWorkItems, channel, ct);

        logger.LogInformation(
            "Availability patch: {Accepted} accepted, {Skipped} skipped out of {Total} expanded rows",
            accepted, skipped, expanded.Count);

        return new AvailabilityPatchResult(accepted, skipped);
    }

    /// <summary>Writes one work item per buffered new row, reading each row's
    /// id now that a SaveChangesAsync has persisted it, then clears the buffer.</summary>
    private static async Task FlushNewRowWorkItemsAsync(
        List<(AvailabilityAggregate Row, SheetWriteContext? Sheet)> pendingNewRowWorkItems,
        AvailabilityChannel channel, CancellationToken ct)
    {
        foreach (var (row, sheet) in pendingNewRowWorkItems)
            await channel.Writer.WriteAsync(new AvailabilityWorkItem(row.Id, sheet), ct);
        pendingNewRowWorkItems.Clear();
    }

    /// <summary>Times empty means slotless: a single row at midnight of From
    /// (the 00:00 sentinel). Otherwise one row per time in Times at
    /// From.ToDateTime(time).</summary>
    private static IEnumerable<DateTimeOffset> ExpandTimeslots(AvailabilityPatchItem item)
    {
        if (item.Times.Count == 0)
        {
            yield return new DateTimeOffset(item.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            yield break;
        }

        foreach (var time in item.Times)
            yield return new DateTimeOffset(item.From.ToDateTime(time), TimeSpan.Zero);
    }

    /// <summary>A null incoming signal is equal by definition ("don't care");
    /// only non-null incoming signals are compared against the row's current value.</summary>
    private static bool ValuesWouldChange(AvailabilityAggregate row, int? incomingVacancies, bool? incomingStopSales)
    {
        var vacanciesChanged = incomingVacancies is not null && incomingVacancies != row.Vacancies;
        var stopSalesChanged = incomingStopSales is not null && incomingStopSales != row.StopSales;
        return vacanciesChanged || stopSalesChanged;
    }
}
