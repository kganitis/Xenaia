namespace Xenaia.Core.Notifications;

public interface INotificationService
{
    Task SendAsync(Notification notification, CancellationToken ct = default);
}
