namespace Xenaia.Domain.Bookings.Providers;

/// <summary>Transport or protocol failure talking to the booking system.
/// Callers treat any of these as a retryable sync failure.</summary>
public class BookingSystemException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>A write referenced an entity the booking system does not know
/// (permanent failure, no retry).</summary>
public sealed class BookingSystemEntityNotFoundException(string message)
    : BookingSystemException(message);
