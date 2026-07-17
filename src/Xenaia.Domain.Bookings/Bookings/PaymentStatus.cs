namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>Canonical payment lifecycle vocabulary; adapters map onto it.</summary>
public enum PaymentStatus
{
    Unknown = 0,
    Pending = 1,
    Captured = 2,
    Refunded = 3,
    Failed = 4,
}
