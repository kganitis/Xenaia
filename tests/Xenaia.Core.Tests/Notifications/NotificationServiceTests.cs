using Microsoft.Extensions.Logging.Abstractions;
using Xenaia.Core.Notifications;

namespace Xenaia.Core.Tests.Notifications;

public class NotificationServiceTests
{
    private sealed class FakeSender(string channel, bool throws = false) : INotificationChannelSender
    {
        public string Channel => channel;
        public List<Notification> Sent { get; } = [];

        public Task SendAsync(Notification notification, CancellationToken ct = default)
        {
            if (throws) throw new InvalidOperationException($"{channel} is down");
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private static readonly Notification SampleNotification =
        new("Booking received", "MT-1001 for Meridian Trails kayak rental");

    [Fact]
    public async Task Fans_out_to_every_registered_channel()
    {
        var chat = new FakeSender("chat");
        var email = new FakeSender("email");
        var service = new NotificationService([chat, email], NullLogger<NotificationService>.Instance);

        await service.SendAsync(SampleNotification);

        Assert.Single(chat.Sent);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task A_failing_channel_does_not_silence_the_others()
    {
        var broken = new FakeSender("broken", throws: true);
        var healthy = new FakeSender("healthy");
        var service = new NotificationService([broken, healthy], NullLogger<NotificationService>.Instance);

        await service.SendAsync(SampleNotification);

        Assert.Single(healthy.Sent);
    }

    [Fact]
    public async Task Caller_cancellation_stops_the_fan_out()
    {
        using var cts = new CancellationTokenSource();
        var cancelling = new CancellingSender(cts);
        var never = new FakeSender("never");
        var service = new NotificationService([cancelling, never], NullLogger<NotificationService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SendAsync(SampleNotification, cts.Token));

        Assert.Empty(never.Sent);
    }

    private sealed class CancellingSender(CancellationTokenSource cts) : INotificationChannelSender
    {
        public string Channel => "cancelling";

        public Task SendAsync(Notification notification, CancellationToken ct = default)
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
