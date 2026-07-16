using Microsoft.Extensions.Logging;

namespace Xenaia.Core.Notifications;

/// <summary>
/// Fans a notification out to every registered channel. One dead channel
/// must never silence the others: failures are logged, not thrown.
/// </summary>
public sealed class NotificationService(
    IEnumerable<INotificationChannelSender> senders,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task SendAsync(Notification notification, CancellationToken ct = default)
    {
        foreach (var sender in senders)
        {
            try
            {
                await sender.SendAsync(notification, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Caller cancellation is not a channel failure; stop the fan-out.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification channel {Channel} failed for {Title}",
                    sender.Channel, notification.Title);
            }
        }
    }
}
