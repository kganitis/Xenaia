using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Sync;

/// <summary>
/// The durable queue for booking-system writes. Locally originated bookings
/// are persisted as requests, not Bookings: the booking system is the system
/// of record and assigns codes, so the local Booking row is born only when
/// the confirmed snapshot comes back and is ingested.
/// Payload: for Create the serialized BookingDraft JSON; for Cancel the code.
/// </summary>
public sealed class OutboundBookingRequest : Entity<int>, ISyncTracked
{
    public OutboundBookingKind Kind { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public SyncState Sync { get; private set; } = SyncState.Pending;

    private OutboundBookingRequest(int id) : base(id) { }

    public static OutboundBookingRequest ForCreate(string draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson))
            throw new ArgumentException("Create request payload is empty.");
        return new OutboundBookingRequest(0) { Kind = OutboundBookingKind.Create, Payload = draftJson };
    }

    public static OutboundBookingRequest ForCancel(string bookingCode)
    {
        if (string.IsNullOrWhiteSpace(bookingCode))
            throw new ArgumentException("Cancel request needs a booking code.");
        return new OutboundBookingRequest(0) { Kind = OutboundBookingKind.Cancel, Payload = bookingCode };
    }

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at) => Sync = Sync.MarkFailed(error);

    public void RequeueSync() => Sync = Sync.Requeue();
}
