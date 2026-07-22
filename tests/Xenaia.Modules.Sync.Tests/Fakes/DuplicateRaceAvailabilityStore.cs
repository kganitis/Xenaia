using System.Reflection;
using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>
/// Availability store that reproduces a unique-index insert race for one key:
/// the first SaveChangesAsync that would insert a row for the racer's key throws
/// <see cref="DuplicateAvailabilityException"/>, as if a concurrent writer had
/// inserted that row (the "racer") in the meantime. The conflicting pending add
/// is dropped (mirroring EfAvailabilityStore detaching the failed entry) and the
/// racer row becomes visible to the next GetByKeysAsync, so the patch service's
/// retry can merge onto it. Later saves behave normally.
/// </summary>
internal sealed class DuplicateRaceAvailabilityStore(AvailabilityAggregate racerRow) : IAvailabilityStore
{
    private readonly Dictionary<AvailabilityKey, AvailabilityAggregate> _committed = [];
    private readonly List<AvailabilityAggregate> _pendingAdds = [];
    private readonly AvailabilityAggregate _racerRow = racerRow;
    private readonly AvailabilityKey _raceKey = KeyOf(racerRow);
    private bool _raceFired;
    private int _nextId = 1;

    public int GetByKeysCallCount { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public Task<IReadOnlyList<AvailabilityAggregate>> GetByKeysAsync(
        IReadOnlyCollection<AvailabilityKey> keys, CancellationToken ct)
    {
        GetByKeysCallCount++;
        IReadOnlyList<AvailabilityAggregate> result = keys
            .Select(k => _committed.GetValueOrDefault(k))
            .Where(a => a is not null).Select(a => a!).Distinct().ToList();
        return Task.FromResult(result);
    }

    public Task<AvailabilityAggregate?> GetByIdAsync(int id, CancellationToken ct)
        => Task.FromResult(_committed.Values.FirstOrDefault(a => a.Id == id));

    public Task AddAsync(AvailabilityAggregate availability, CancellationToken ct)
    {
        _pendingAdds.Add(availability);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(int id, CancellationToken ct) => Task.FromResult(false);

    public Task<IReadOnlyList<AvailabilityAggregate>> GetPendingAsync(int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AvailabilityAggregate>>([]);

    public Task<int> ResetProcessingAsync(CancellationToken ct) => Task.FromResult(0);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveChangesCallCount++;

        var conflict = _pendingAdds.FirstOrDefault(a => KeyOf(a) == _raceKey);
        if (conflict is not null && !_raceFired)
        {
            _raceFired = true;
            // The racer committed its row first; the store detaches our
            // now-doomed insert and surfaces the domain exception.
            AssignId(_racerRow, _nextId++);
            _committed[_raceKey] = _racerRow;
            _pendingAdds.Remove(conflict);
            throw new DuplicateAvailabilityException([_raceKey]);
        }

        foreach (var add in _pendingAdds)
        {
            AssignId(add, _nextId++);
            _committed[KeyOf(add)] = add;
        }
        _pendingAdds.Clear();
        return Task.CompletedTask;
    }

    private static AvailabilityKey KeyOf(AvailabilityAggregate a) =>
        new(a.ExternalProductId, a.ExternalOptionId, a.TimeslotAt);

    private static void AssignId(AvailabilityAggregate a, int id)
    {
        var field = typeof(Entity<int>).GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not locate Availability's Id backing field.");
        field.SetValue(a, id);
    }
}
