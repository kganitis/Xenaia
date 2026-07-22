namespace Xenaia.Modules.Sync.Bookings;

/// <summary>Thrown by <see cref="OutboundBookingEnqueuer.EnqueueCancelAsync"/>
/// when no local booking carries the given code. The cancel endpoint maps this
/// to 404. Derives from <see cref="InvalidOperationException"/> so existing
/// callers that catch the base type keep working.</summary>
public sealed class UnknownBookingException(string code)
    : InvalidOperationException($"No local booking with code '{code}'.")
{
    public string Code { get; } = code;
}

/// <summary>Thrown by <see cref="OutboundBookingEnqueuer.EnqueueCancelAsync"/>
/// when the local booking is already cancelled. The cancel endpoint maps this
/// to 409. Derives from <see cref="InvalidOperationException"/> so existing
/// callers that catch the base type keep working.</summary>
public sealed class BookingAlreadyCancelledException(string code)
    : InvalidOperationException($"Booking '{code}' is already cancelled.")
{
    public string Code { get; } = code;
}
