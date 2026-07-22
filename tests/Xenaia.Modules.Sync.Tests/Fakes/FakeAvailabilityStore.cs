using System.Reflection;
using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
// Alias needed: a sibling test file declares namespace
// Xenaia.Modules.Sync.Tests.Availability, which shadows the bare aggregate
// type name Xenaia.Domain.Bookings.Availabilities.Availability project-wide
// (namespace member lookup walks the enclosing Xenaia.Modules.Sync.Tests
// namespace, where "Availability" now names that nested namespace).
using AvailabilityAggregate = Xenaia.Domain.Bookings.Availabilities.Availability;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>In-memory IAvailabilityStore for AvailabilityPatchService tests.
/// Mimics EF's deferred key generation: AddAsync buffers the entity without
/// an id (it isn't visible to GetByKeysAsync, matching a real DB query that
/// can't see uncommitted adds); SaveChangesAsync is what assigns the
/// incremental id and makes the row visible. Keyed by AvailabilityKey once
/// committed; records call counts for GetByKeysAsync and SaveChangesAsync.</summary>
internal sealed class FakeAvailabilityStore : IAvailabilityStore
{
    private readonly Dictionary<AvailabilityKey, AvailabilityAggregate> _byKey = [];
    private readonly List<AvailabilityAggregate> _pendingAdds = [];
    private int _nextId = 1;

    public int GetByKeysCallCount { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyCollection<AvailabilityAggregate> All => _byKey.Values;

    /// <summary>Seeds a pre-existing row (as if a prior call had already
    /// created and persisted it), assigning it the next incremental id.</summary>
    public void Seed(AvailabilityAggregate availability)
    {
        AssignId(availability, _nextId++);
        _byKey[KeyOf(availability)] = availability;
    }

    public Task<IReadOnlyList<AvailabilityAggregate>> GetByKeysAsync(
        IReadOnlyCollection<AvailabilityKey> keys, CancellationToken ct)
    {
        GetByKeysCallCount++;
        IReadOnlyList<AvailabilityAggregate> result = keys
            .Select(k => _byKey.GetValueOrDefault(k))
            .Where(a => a is not null)
            .Select(a => a!)
            .Distinct()
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(AvailabilityAggregate availability, CancellationToken ct)
    {
        // No id, not yet in _byKey: matches EF, where an Added-but-unsaved
        // entity has no database-generated key and is not query-visible.
        _pendingAdds.Add(availability);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(int id, CancellationToken ct)
    {
        var row = _byKey.Values.FirstOrDefault(a => a.Id == id);
        if (row is null || row.Sync.Status != SyncStatus.Pending)
            return Task.FromResult(false);
        row.ClaimForSync();
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<AvailabilityAggregate>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        IReadOnlyList<AvailabilityAggregate> result = _byKey.Values
            .Where(a => a.Sync.Status == SyncStatus.Pending)
            .OrderBy(a => a.Id)
            .Take(batchSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> ResetProcessingAsync(CancellationToken ct)
    {
        var stuck = _byKey.Values.Where(a => a.Sync.Status == SyncStatus.Processing).ToList();
        foreach (var row in stuck)
            row.RequeueSync();
        return Task.FromResult(stuck.Count);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        foreach (var availability in _pendingAdds)
        {
            AssignId(availability, _nextId++);
            _byKey[KeyOf(availability)] = availability;
        }
        _pendingAdds.Clear();
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }

    private static AvailabilityKey KeyOf(AvailabilityAggregate availability) =>
        new(availability.ExternalProductId, availability.ExternalOptionId, availability.TimeslotAt);

    private static void AssignId(AvailabilityAggregate availability, int id)
    {
        var field = typeof(Entity<int>).GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Could not locate Availability's Id backing field for test id assignment.");
        field.SetValue(availability, id);
    }
}
