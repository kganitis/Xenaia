namespace Xenaia.Core.Notifications;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed record Notification(
    string Title,
    string Body,
    NotificationSeverity Severity = NotificationSeverity.Info,
    IReadOnlyDictionary<string, string>? Metadata = null);
