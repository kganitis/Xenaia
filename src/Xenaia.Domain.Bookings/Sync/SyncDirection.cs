namespace Xenaia.Domain.Bookings.Sync;

/// <summary>Origin of a synced entity: pulled from the booking system or created locally.</summary>
public enum SyncDirection
{
    Inbound = 0,
    Outbound = 1,
}
