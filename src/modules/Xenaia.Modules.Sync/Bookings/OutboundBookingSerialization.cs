using System.Text.Json;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>The single serializer contract for outbound booking-request
/// payloads: System.Text.Json web defaults, shared by the enqueuer (writes
/// the draft) and the pusher (reads it back), so a draft round-trips byte for
/// byte through the durable queue.</summary>
internal static class OutboundBookingSerialization
{
    public static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web);
}
