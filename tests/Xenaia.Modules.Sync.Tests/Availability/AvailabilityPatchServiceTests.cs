using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Tests.Fakes;
// Alias needed: this file's own namespace ends in "Availability", which
// shadows the bare aggregate type name Xenaia.Domain.Bookings.Availabilities.Availability.
using AvailabilityAggregate = Xenaia.Domain.Bookings.Availabilities.Availability;

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

        var workItems = Drain(channel);
        Assert.Equal(2, workItems.Count);
        Assert.All(workItems, w => Assert.Null(w.Sheet));
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
        Assert.Null(workItem.Sheet.GetRowRange);
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
        FakeAvailabilityStore store, out AvailabilityChannel channel, SyncOptions? options = null)
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
