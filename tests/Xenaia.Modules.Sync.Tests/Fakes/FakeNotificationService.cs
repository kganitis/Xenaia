using Xenaia.Core.Notifications;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>In-memory INotificationService that records every notification it
/// is asked to send, so tests can assert the fan-out fired exactly once with
/// the expected severity and metadata.</summary>
internal sealed class FakeNotificationService : INotificationService
{
    private readonly List<Notification> _sent = [];

    public IReadOnlyList<Notification> Sent => _sent;

    public Task SendAsync(Notification notification, CancellationToken ct = default)
    {
        _sent.Add(notification);
        return Task.CompletedTask;
    }
}
