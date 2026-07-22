using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>In-memory ISyncCheckpointStore keyed by checkpoint name.</summary>
internal sealed class FakeSyncCheckpointStore : ISyncCheckpointStore
{
    private readonly Dictionary<string, DateTimeOffset> _byName = [];

    public void Seed(string name, DateTimeOffset value) => _byName[name] = value;

    public Task<DateTimeOffset?> GetAsync(string name, CancellationToken ct) =>
        Task.FromResult(_byName.TryGetValue(name, out var value) ? value : (DateTimeOffset?)null);

    public Task SetAsync(string name, DateTimeOffset value, CancellationToken ct)
    {
        _byName[name] = value;
        return Task.CompletedTask;
    }
}
