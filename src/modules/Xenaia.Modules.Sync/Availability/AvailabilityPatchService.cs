using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

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

        // Multi-day rows are rejected fail-closed before any store work:
        // ExpandTimeslots keys every timeslot off From alone, so a From != To
        // row would silently update one day while reporting the whole range
        // synced. One row per day is required (spec 6.1).
        foreach (var item in items)
            if (item.From != item.To)
                throw new ArgumentException(
                    "multi-day patch items are not supported; submit one row per day",
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
        // run. Each buffered row keeps its key and source so a duplicate-insert
        // race can be retried as a merge (see SaveBatchAsync). Existing rows
        // already carry a real id and can be written as they're accepted.
        var pendingNewRows = new List<PendingNewRow>();

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

            ApplyMerge(row, source);
            accepted++;

            var sheet = spreadsheetId is not null
                ? new SheetWriteContext(spreadsheetId, source.PatchStatusRange)
                : null;

            if (isNew)
                pendingNewRows.Add(new PendingNewRow(key, row, source, sheet));
            else
                await channel.Writer.WriteAsync(new AvailabilityWorkItem(row.Id, sheet), ct);

            pendingSaves++;
            if (pendingSaves >= SaveBatchSize)
            {
                await SaveBatchAsync(pendingNewRows, ct);
                pendingSaves = 0;
            }
        }

        if (pendingSaves > 0)
            await SaveBatchAsync(pendingNewRows, ct);

        logger.LogInformation(
            "Availability patch: {Accepted} accepted, {Skipped} skipped out of {Total} expanded rows",
            accepted, skipped, expanded.Count);

        return new AvailabilityPatchResult(accepted, skipped);
    }

    /// <summary>Merges an item's non-null signals onto a row and requeues it to
    /// Pending. Requeue is only a legal transition from Synced/Failed/Processing;
    /// a Pending row (new or unclaimed) just keeps its state as-is.</summary>
    private static void ApplyMerge(AvailabilityAggregate row, AvailabilityPatchItem source)
    {
        if (source.Vacancies is not null)
            row.SetVacancies(source.Vacancies.Value);
        if (source.StopSales is not null)
            row.SetStopSales(source.StopSales.Value);
        if (row.Sync.Status != SyncStatus.Pending)
            row.RequeueSync();
    }

    /// <summary>
    /// Saves the current batch, then flushes one work item per buffered new
    /// row (reading each row's id now that the save assigned it) and clears the
    /// buffer. On a duplicate-insert race (another writer inserted a row with
    /// the same key between our key lookup and our save), the store surfaces a
    /// <see cref="DuplicateAvailabilityException"/>; we retry once by re-fetching
    /// the batch's keys and converting each conflicted add into a value-aware
    /// merge onto the row the racer already inserted. A second duplicate failure
    /// propagates (spec section 12).
    /// </summary>
    private async Task SaveBatchAsync(List<PendingNewRow> pendingNewRows, CancellationToken ct)
    {
        try
        {
            await store.SaveChangesAsync(ct);
        }
        catch (DuplicateAvailabilityException)
        {
            await MergeConflictsAsync(pendingNewRows, ct);
            await store.SaveChangesAsync(ct);
        }

        foreach (var pending in pendingNewRows)
            await channel.Writer.WriteAsync(new AvailabilityWorkItem(pending.Row.Id, pending.Sheet), ct);
        pendingNewRows.Clear();
    }

    /// <summary>Re-fetches the batch's keys (the racer's rows now exist and come
    /// back tracked) and merges each conflicted add's signals onto the existing
    /// row, retargeting its buffered work item at that row. Non-conflicting adds
    /// (not yet in the store) are left to insert on the retry save.</summary>
    private async Task MergeConflictsAsync(List<PendingNewRow> pendingNewRows, CancellationToken ct)
    {
        var keys = pendingNewRows.Select(p => p.Key).Distinct().ToList();
        var existingByKey = (await store.GetByKeysAsync(keys, ct))
            .ToDictionary(a => new AvailabilityKey(a.ExternalProductId, a.ExternalOptionId, a.TimeslotAt));

        for (var i = 0; i < pendingNewRows.Count; i++)
        {
            var pending = pendingNewRows[i];
            if (!existingByKey.TryGetValue(pending.Key, out var existing))
                continue; // not the conflicting row: still a pending insert

            ApplyMerge(existing, pending.Source);
            pendingNewRows[i] = pending with { Row = existing };
        }
    }

    /// <summary>A new row buffered until its batch save assigns its id, kept
    /// with its key and source so a duplicate-insert race can be merged.</summary>
    private sealed record PendingNewRow(
        AvailabilityKey Key, AvailabilityAggregate Row,
        AvailabilityPatchItem Source, SheetWriteContext? Sheet);

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
