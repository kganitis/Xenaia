using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Tests;

public class CoreContractTests
{
    private sealed record ProbeEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class ProbeAggregate(int id) : AggregateRoot<int>(id)
    {
        public void RaiseProbe(DateTimeOffset at) => Raise(new ProbeEvent(at));
    }

    [Fact]
    public void Aggregate_roots_expose_events_through_the_non_generic_interface()
    {
        var aggregate = new ProbeAggregate(1);
        aggregate.RaiseProbe(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero));

        IHasDomainEvents view = aggregate;

        Assert.Single(view.DomainEvents);
        Assert.Single(view.DequeueDomainEvents());
        Assert.Empty(view.DomainEvents);
    }
}
