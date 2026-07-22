using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Tests.Fakes;

namespace Xenaia.Modules.Sync.Tests.Availability;

public class AvailabilityPatchServiceTests
{
    private const int ProductId = 100;
    private const int OptionId = 7;
    private static readonly DateOnly Day = new(2026, 8, 1);

    [Fact]
    public async Task New_key_creates_pending_rows_and_queues_a_work_item_per_time()
    {
        var store = new FakeAvailabilityStore();
        var service = CreateService(store, out var channel);
        var times = new TimeOnly[] { new(9, 0), new(14, 30) };
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, times, 5, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, store.GetByKeysCallCount);

        var created = store.All.OrderBy(a => a.TimeslotAt).ToList();
        Assert.Equal(2, created.Count);
        Assert.Equal(TimeslotAt(times[0]), created[0].TimeslotAt);
        Assert.Equal(TimeslotAt(times[1]), created[1].TimeslotAt);
        Assert.All(created, a => Assert.Equal(SyncStatus.Pending, a.Sync.Status));
        Assert.All(created, a => Assert.NotEqual(0, a.Id)); // real ids: SaveChangesAsync ran before we asserted

        var workItems = Drain(channel);
        Assert.Equal(2, workItems.Count);
        Assert.All(workItems, w => Assert.Null(w.Sheet));
        // The work item must carry the row's real persisted id (assigned by
        // SaveChangesAsync), never the pre-save placeholder of 0: the
        // processor claims by id, and id 0 would never match a real row.
        var createdIds = created.Select(a => a.Id).ToHashSet();
        Assert.All(workItems, w => Assert.NotEqual(0, w.AvailabilityId));
        Assert.Equal(createdIds, workItems.Select(w => w.AvailabilityId).ToHashSet());
    }

    [Fact]
    public async Task Existing_pending_row_with_identical_values_is_skipped()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 5, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(Drain(channel));
        Assert.Equal(SyncStatus.Pending, existing.Sync.Status);
        Assert.Equal(5, existing.Vacancies);
    }

    [Fact]
    public async Task Existing_pending_row_with_different_values_is_mutated_in_place_without_requeue()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 9, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(9, existing.Vacancies);
        // Still Pending, not re-requeued: RequeueSync() targeting Pending is
        // only a legal transition from Synced/Failed/Processing, never from
        // Pending itself, so a Pending row just gets its setters called.
        Assert.Equal(SyncStatus.Pending, existing.Sync.Status);

        var workItem = Assert.Single(Drain(channel));
        Assert.Equal(existing.Id, workItem.AvailabilityId);
    }

    [Fact]
    public async Task Existing_synced_row_with_identical_values_is_skipped()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5, synced: true);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 5, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(Drain(channel));
        Assert.Equal(SyncStatus.Synced, existing.Sync.Status);
    }

    [Fact]
    public async Task Existing_synced_row_with_different_vacancies_is_merged_and_requeued()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5, synced: true);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 9, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(9, existing.Vacancies);
        Assert.Equal(SyncStatus.Pending, existing.Sync.Status);

        var workItem = Assert.Single(Drain(channel));
        Assert.Equal(existing.Id, workItem.AvailabilityId);
    }

    [Fact]
    public async Task Existing_processing_row_is_always_skipped_even_with_force()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5, processing: true);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 9, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);
        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(Drain(channel));
        Assert.Equal(5, existing.Vacancies);
        Assert.Equal(SyncStatus.Processing, existing.Sync.Status);

        var forced = await service.EnqueueAsync([item], null, true, CancellationToken.None);
        Assert.Equal(0, forced.Accepted);
        Assert.Equal(1, forced.Skipped);
        Assert.Empty(Drain(channel));
        Assert.Equal(5, existing.Vacancies);
        Assert.Equal(SyncStatus.Processing, existing.Sync.Status);
    }

    [Fact]
    public async Task Null_incoming_vacancies_only_compares_and_merges_stop_sales()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5, stopSales: false, synced: true);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], null, true, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(5, existing.Vacancies); // untouched: incoming vacancies was null ("don't care")
        Assert.True(existing.StopSales);
        Assert.Equal(SyncStatus.Pending, existing.Sync.Status);
        Assert.Single(Drain(channel));
    }

    [Fact]
    public async Task Force_bypasses_the_identical_value_skip_but_not_the_processing_skip()
    {
        var store = new FakeAvailabilityStore();
        var existing = Seed(store, new TimeOnly(9, 0), vacancies: 5, synced: true);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 5, null, null);

        var result = await service.EnqueueAsync([item], null, true, CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(SyncStatus.Pending, existing.Sync.Status);
        Assert.Single(Drain(channel));
    }

    [Fact]
    public async Task Slotless_item_creates_a_single_row_at_midnight_of_from()
    {
        var store = new FakeAvailabilityStore();
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [], 3, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        var created = Assert.Single(store.All);
        Assert.Equal(new DateTimeOffset(Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), created.TimeslotAt);
        Assert.Single(Drain(channel));
    }

    [Fact]
    public async Task Multi_day_item_is_rejected_before_any_store_work()
    {
        var store = new FakeAvailabilityStore();
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(
            ProductId, OptionId, Day, Day.AddDays(1), [new TimeOnly(9, 0)], 5, null, null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnqueueAsync([item], null, false, CancellationToken.None));

        Assert.Contains("multi-day patch items are not supported", ex.Message);
        // Fail closed before touching the store: no lookup, no save, no row.
        Assert.Equal(0, store.GetByKeysCallCount);
        Assert.Equal(0, store.SaveChangesCallCount);
        Assert.Empty(store.All);
        Assert.Empty(Drain(channel));
    }

    [Fact]
    public async Task Duplicate_insert_race_is_retried_as_a_merge_onto_the_existing_row()
    {
        // A concurrent writer inserted a Synced row for this key first.
        var racer = AvailabilityAggregate.ForTimeslot(ProductId, OptionId, TimeslotAt(new TimeOnly(9, 0)));
        racer.SetVacancies(3);
        racer.ClaimForSync();
        racer.MarkSynced(DateTimeOffset.UtcNow);
        var store = new DuplicateRaceAvailabilityStore(racer);
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 9, null, null);

        var result = await service.EnqueueAsync([item], null, false, CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Skipped);
        // The insert failed once, then the retry saved the merge.
        Assert.Equal(2, store.SaveChangesCallCount);
        // The merge landed on the racer's row: value applied, requeued to Pending.
        Assert.Equal(9, racer.Vacancies);
        Assert.Equal(SyncStatus.Pending, racer.Sync.Status);
        // The work item carries the now-existing row's real id, not the dropped add.
        var workItem = Assert.Single(Drain(channel));
        Assert.NotEqual(0, workItem.AvailabilityId);
        Assert.Equal(racer.Id, workItem.AvailabilityId);
    }

    [Fact]
    public async Task More_than_max_batch_size_throws()
    {
        var store = new FakeAvailabilityStore();
        var options = new SyncOptions { Availability = new AvailabilityOptions { MaxBatchSize = 2 } };
        var service = CreateService(store, out var channel, options);
        var items = Enumerable.Range(0, 3)
            .Select(i => new AvailabilityPatchItem(
                ProductId, OptionId, Day, Day, [new TimeOnly(9, 0).Add(TimeSpan.FromMinutes(i))], 1, null, null))
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnqueueAsync(items, null, false, CancellationToken.None));
        Assert.Equal(0, store.GetByKeysCallCount);
        Assert.Empty(Drain(channel));
    }

    [Fact]
    public async Task Sheet_context_is_propagated_to_the_work_item()
    {
        var store = new FakeAvailabilityStore();
        var service = CreateService(store, out var channel);
        var item = new AvailabilityPatchItem(
            ProductId, OptionId, Day, Day, [new TimeOnly(9, 0)], 4, null, "PatchSheet!A5");

        await service.EnqueueAsync([item], "sheet-123", false, CancellationToken.None);

        var workItem = Assert.Single(Drain(channel));
        Assert.NotNull(workItem.Sheet);
        Assert.Equal("sheet-123", workItem.Sheet!.SpreadsheetId);
        Assert.Equal("PatchSheet!A5", workItem.Sheet.PatchStatusRange);
    }

    [Fact]
    public async Task SaveChangesAsync_is_batched_every_fifty_accepted_rows()
    {
        var store = new FakeAvailabilityStore();
        var options = new SyncOptions { Availability = new AvailabilityOptions { MaxBatchSize = 200 } };
        var service = CreateService(store, out var channel, options);
        var items = Enumerable.Range(0, 120)
            .Select(i => new AvailabilityPatchItem(
                ProductId, OptionId, Day, Day, [new TimeOnly(0, 0).Add(TimeSpan.FromMinutes(i))], 1, null, null))
            .ToList();

        var result = await service.EnqueueAsync(items, null, false, CancellationToken.None);

        Assert.Equal(120, result.Accepted);
        Assert.Equal(3, store.SaveChangesCallCount); // 50 + 50 + 20
        Drain(channel);
    }

    private static DateTimeOffset TimeslotAt(TimeOnly time) =>
        new(Day.ToDateTime(time), TimeSpan.Zero);

    private static AvailabilityAggregate Seed(
        FakeAvailabilityStore store, TimeOnly time,
        int? vacancies = null, bool? stopSales = null,
        bool synced = false, bool processing = false)
    {
        var row = AvailabilityAggregate.ForTimeslot(ProductId, OptionId, TimeslotAt(time));
        if (vacancies is not null)
            row.SetVacancies(vacancies.Value);
        if (stopSales is not null)
            row.SetStopSales(stopSales.Value);
        if (synced)
        {
            row.ClaimForSync();
            row.MarkSynced(DateTimeOffset.UtcNow);
        }
        else if (processing)
        {
            row.ClaimForSync();
        }

        store.Seed(row);
        return row;
    }

    private static AvailabilityPatchService CreateService(
        IAvailabilityStore store, out AvailabilityChannel channel, SyncOptions? options = null)
    {
        channel = new AvailabilityChannel(1000);
        return new AvailabilityPatchService(
            store, channel, Options.Create(options ?? new SyncOptions()),
            NullLogger<AvailabilityPatchService>.Instance);
    }

    private static List<AvailabilityWorkItem> Drain(AvailabilityChannel channel)
    {
        var items = new List<AvailabilityWorkItem>();
        while (channel.Reader.TryRead(out var item))
            items.Add(item);
        return items;
    }
}
