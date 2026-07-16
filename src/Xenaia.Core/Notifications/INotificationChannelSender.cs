namespace Xenaia.Core.Notifications;

/// <summary>One delivery channel (chat webhook, hub, email). Adapters implement this.</summary>
public interface INotificationChannelSender
{
    string Channel { get; }
    Task SendAsync(Notification notification, CancellationToken ct = default);
}
