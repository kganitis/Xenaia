namespace Xenaia.Domain.Bookings.Sync;

/// <summary>Where an entity stands against the external booking system.</summary>
public enum SyncStatus
{
    Pending = 0,
    Processing = 1,
    Synced = 2,
    Failed = 3,
}
