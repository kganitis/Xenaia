using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Availabilities;

/// <summary>
/// One timeslot's availability state for a product option. Null Vacancies
/// or StopSales means "this signal is not being updated", mirroring the
/// partial-update semantics of sheet-driven availability.
/// </summary>
public sealed class Availability : AggregateRoot<int>, ISyncTracked
{
    public int ExternalProductId { get; private set; }

    public int ExternalOptionId { get; private set; }

    public DateTimeOffset TimeslotAt { get; private set; }

    public int? Vacancies { get; private set; }

    public bool? StopSales { get; private set; }

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private Availability(int id) : base(id) { }

    public static Availability ForTimeslot(
        int externalProductId, int externalOptionId, DateTimeOffset timeslotAt) =>
        new(0)
        {
            ExternalProductId = externalProductId,
            ExternalOptionId = externalOptionId,
            TimeslotAt = timeslotAt,
        };

    public void SetVacancies(int vacancies)
    {
        if (vacancies < 0)
            throw new AvailabilityRuleViolationException("Vacancies cannot be negative.");
        Vacancies = vacancies;
    }

    public void SetStopSales(bool stopSales) => StopSales = stopSales;

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
