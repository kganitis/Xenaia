using System.Threading.Channels;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>
/// Bounded, single-reader wake-up channel for outbound booking requests. The
/// item is the durable request row's id; the DB row is the durable queue and
/// this channel only wakes the pusher, so FullMode.Wait applies backpressure
/// rather than dropping a notification.
/// </summary>
public sealed class BookingChannel
{
    private readonly Channel<int> _channel;

    public BookingChannel(int capacity)
    {
        _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public ChannelWriter<int> Writer => _channel.Writer;

    public ChannelReader<int> Reader => _channel.Reader;
}
