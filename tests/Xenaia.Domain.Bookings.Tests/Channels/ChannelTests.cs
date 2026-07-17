using Xenaia.Domain.Bookings.Channels;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Channels;

public class ChannelTests
{
    [Fact]
    public void Define_creates_an_active_pending_channel()
    {
        var channel = Channel.Define("PARTNER-A", "Partner A Storefront");

        Assert.Equal("PARTNER-A", channel.Code);
        Assert.True(channel.IsActive);
        Assert.Equal(SyncStatus.Pending, channel.Sync.Status);
    }

    [Fact]
    public void Define_rejects_blank_code()
    {
        Assert.Throws<ChannelRuleViolationException>(() => Channel.Define(" "));
    }

    [Fact]
    public void Deactivate_and_Activate_toggle()
    {
        var channel = Channel.Define("PARTNER-A");

        channel.Deactivate();
        Assert.False(channel.IsActive);

        channel.Activate();
        Assert.True(channel.IsActive);
    }
}
