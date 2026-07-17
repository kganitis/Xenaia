namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>How a booking was created in the external booking system.</summary>
public enum BookingType
{
    Unknown = 0,
    Landing = 1,
    Admin = 2,
    Api = 3,
}
