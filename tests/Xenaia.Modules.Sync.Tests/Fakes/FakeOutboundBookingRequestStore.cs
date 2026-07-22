using System.Reflection;
using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>In-memory IOutboundBookingRequestStore. Mimics EF's deferred key
/// generation (like FakeAvailabilityStore): AddAsync buffers the request
/// without an id until SaveChangesAsync assigns the incremental id and commits
/// it. TryClaimAsync flips a committed Pending row to Processing exactly once;
/// GetByIdAsync returns the committed instance. Records SaveChangesAsync call
/// count.</summary>
internal sealed class FakeOutboundBookingRequestStore : IOutboundBookingRequestStore
{
    private readonly Dictionary<int, OutboundBookingRequest> _byId = [];
    private readonly List<OutboundBookingRequest> _pendingAdds = [];
    private int _nextId = 1;

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyCollection<OutboundBookingRequest> All => _byId.Values;

    /// <summary>Seeds a pre-existing committed request (as if a prior call had
    /// already persisted it), assigning it the next incremental id.</summary>
    public OutboundBookingRequest Seed(OutboundBookingRequest request)
    {
        AssignId(request, _nextId++);
        _byId[request.Id] = request;
        return request;
    }

    public Task AddAsync(OutboundBookingRequest request, CancellationToken ct)
    {
        // No id, not yet in _byId: matches EF, where an Added-but-unsaved
        // entity has no database-generated key and is not query-visible.
        _pendingAdds.Add(request);
        return Task.CompletedTask;
    }

    public Task<OutboundBookingRequest?> GetByIdAsync(int id, CancellationToken ct)
        => Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<bool> TryClaimAsync(int id, CancellationToken ct)
    {
        if (!_byId.TryGetValue(id, out var request) || request.Sync.Status != SyncStatus.Pending)
            return Task.FromResult(false);
        request.ClaimForSync();
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<OutboundBookingRequest>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        IReadOnlyList<OutboundBookingRequest> result = _byId.Values
            .Where(r => r.Sync.Status == SyncStatus.Pending)
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> ResetProcessingAsync(CancellationToken ct)
    {
        var stuck = _byId.Values.Where(r => r.Sync.Status == SyncStatus.Processing).ToList();
        foreach (var request in stuck)
            request.RequeueSync();
        return Task.FromResult(stuck.Count);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        foreach (var request in _pendingAdds)
        {
            AssignId(request, _nextId++);
            _byId[request.Id] = request;
        }
        _pendingAdds.Clear();
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }

    private static void AssignId(Entity<int> entity, int id)
    {
        var field = typeof(Entity<int>).GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Could not locate Entity<int>'s Id backing field for test id assignment.");
        field.SetValue(entity, id);
    }
}
