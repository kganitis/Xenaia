using Xenaia.Core.Domain;

namespace Xenaia.Core.Tests.Domain;

public class AggregateRootTests
{
    private sealed record TestEvent(DateTimeOffset OccurredAt) : IDomainEvent;
    private sealed record LabeledEvent(string Label, DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void DoSomething() =>
            Raise(new TestEvent(DateTimeOffset.UnixEpoch));

        public void RaiseLabeled(string label) =>
            Raise(new LabeledEvent(label, DateTimeOffset.UnixEpoch));
    }

    private sealed class OtherAggregate(Guid id) : AggregateRoot<Guid>(id);

    [Fact]
    public void Raised_events_accumulate_in_order()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.RaiseLabeled("first");
        aggregate.RaiseLabeled("second");

        Assert.Equal(2, aggregate.DomainEvents.Count);
        Assert.Equal(["first", "second"],
            aggregate.DomainEvents.Cast<LabeledEvent>().Select(e => e.Label));
    }

    [Fact]
    public void Dequeue_returns_events_and_clears_the_aggregate()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.DoSomething();

        var dequeued = aggregate.DequeueDomainEvents();

        Assert.Single(dequeued);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void Entities_are_equal_by_type_and_id()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new TestAggregate(id), new TestAggregate(id));
        Assert.NotEqual(new TestAggregate(id), new TestAggregate(Guid.NewGuid()));
    }

    [Fact]
    public void Different_entity_types_with_the_same_id_are_not_equal()
    {
        var id = Guid.NewGuid();

        Assert.False(new TestAggregate(id).Equals(new OtherAggregate(id)));
    }

    [Fact]
    public void Equal_entities_have_equal_hash_codes()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new TestAggregate(id).GetHashCode(), new TestAggregate(id).GetHashCode());
        Assert.NotEqual(new TestAggregate(id).GetHashCode(), new OtherAggregate(id).GetHashCode());
    }
}
