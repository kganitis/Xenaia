using Xenaia.Domain.Bookings.Availabilities;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Availabilities;

public class AvailabilityTests
{
    private static readonly DateTimeOffset Slot = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForTimeslot_starts_with_nothing_decided()
    {
        var availability = Availability.ForTimeslot(42, 7, Slot);

        Assert.Equal(42, availability.ExternalProductId);
        Assert.Equal(7, availability.ExternalOptionId);
        Assert.Equal(Slot, availability.TimeslotAt);
        Assert.Null(availability.Vacancies);
        Assert.Null(availability.StopSales);
        Assert.Equal(SyncStatus.Pending, availability.Sync.Status);
    }

    [Fact]
    public void SetVacancies_preserves_stop_sales_and_vice_versa()
    {
        // Null means "not being updated" in the source semantics; setting
        // one signal must not clobber the other.
        var availability = Availability.ForTimeslot(42, 7, Slot);

        availability.SetVacancies(12);
        Assert.Equal(12, availability.Vacancies);
        Assert.Null(availability.StopSales);

        availability.SetStopSales(true);
        Assert.True(availability.StopSales);
        Assert.Equal(12, availability.Vacancies);
    }

    [Fact]
    public void Negative_vacancies_are_rejected()
    {
        var availability = Availability.ForTimeslot(42, 7, Slot);

        Assert.Throws<AvailabilityRuleViolationException>(() => availability.SetVacancies(-1));
    }

    [Fact]
    public void Sync_transitions_work_on_availability()
    {
        var availability = Availability.ForTimeslot(42, 7, Slot);

        availability.ClaimForSync();
        availability.MarkSyncFailed("sheet locked", Slot);

        Assert.Equal(SyncStatus.Failed, availability.Sync.Status);
        Assert.Equal("sheet locked", availability.Sync.Error);
    }
}
