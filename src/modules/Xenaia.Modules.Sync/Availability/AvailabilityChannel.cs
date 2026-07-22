using System.Threading.Channels;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>
/// Bounded, single-reader wake-up channel for availability work items. The
/// DB row is the durable queue; this channel only wakes the processor up, so
/// FullMode.Wait applies backpressure rather than dropping a notification.
/// </summary>
public sealed class AvailabilityChannel
{
    private readonly Channel<AvailabilityWorkItem> _channel;

    public AvailabilityChannel(int capacity)
    {
        _channel = Channel.CreateBounded<AvailabilityWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public ChannelWriter<AvailabilityWorkItem> Writer => _channel.Writer;

    public ChannelReader<AvailabilityWorkItem> Reader => _channel.Reader;
}
