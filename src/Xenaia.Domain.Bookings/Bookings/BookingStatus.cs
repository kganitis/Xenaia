namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>
/// Canonical booking lifecycle vocabulary. Adapters map provider payloads
/// onto these values; Unknown is the fail-safe for unmapped input.
/// </summary>
public enum BookingStatus
{
    Unknown = 0,
    Pending = 1,
    Completed = 2,
    Cancelled = 3,
    Unconfirmed = 4,
    Deprecated = 5,
}
