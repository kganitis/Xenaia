using Xenaia.Core.Domain;
using Xenaia.Core.Outbox;

namespace Xenaia.Core.Tests.Outbox;

public class OutboxMessageTests
{
    public sealed record BookingReceived(string BookingCode, DateTimeOffset OccurredAt) : IDomainEvent;

    [Fact]
    public void Roundtrips_a_domain_event_through_serialization()
    {
        var original = new BookingReceived("MT-1001", DateTimeOffset.UnixEpoch);

        var message = OutboxMessage.From(original);
        var restored = message.ToDomainEvent();

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Captures_occurrence_time_and_starts_unprocessed()
    {
        var message = OutboxMessage.From(new BookingReceived("MT-1001", DateTimeOffset.UnixEpoch));

        Assert.Equal(DateTimeOffset.UnixEpoch, message.OccurredAt);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.Error);
        Assert.NotEqual(Guid.Empty, message.Id);
    }

    [Fact]
    public void Unresolvable_type_deserializes_to_null_instead_of_throwing()
    {
        var message = OutboxMessage.From(new BookingReceived("MT-1001", DateTimeOffset.UnixEpoch))
            with { Type = "Nope.DoesNotExist, Nope" };

        Assert.Null(message.ToDomainEvent());
    }

    [Fact]
    public void Malformed_payload_deserializes_to_null_instead_of_throwing()
    {
        var message = OutboxMessage.From(new BookingReceived("MT-1001", DateTimeOffset.UnixEpoch))
            with { Payload = "{ not valid json" };

        Assert.Null(message.ToDomainEvent());
    }

    [Fact]
    public void Resolvable_type_that_is_not_a_domain_event_deserializes_to_null()
    {
        var message = OutboxMessage.From(new BookingReceived("MT-1001", DateTimeOffset.UnixEpoch))
            with { Type = typeof(string).AssemblyQualifiedName! };

        Assert.Null(message.ToDomainEvent());
    }
}
