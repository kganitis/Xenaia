using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Domain.Bookings.Providers;

/// <summary>
/// Driven port for the external booking system. Owned by Domain.Bookings
/// (deliberate exception to consumer ownership: Sync, Triage's booking-lookup
/// processor, and the future MCP host all consume it, and this project is
/// the only one all of them may reference).
/// </summary>
public interface IBookingSystemProvider
{
    Task<IReadOnlyList<BookingSnapshot>> GetBookingsAsync(
        BookingQuery query, CancellationToken ct);

    /// <summary>Null when the booking system does not know the code.</summary>
    Task<BookingSnapshot?> GetBookingByCodeAsync(string code, CancellationToken ct);

    /// <summary>Creates the booking in the booking system and returns the
    /// confirmed snapshot (the system assigns code and secret code).</summary>
    Task<BookingSnapshot> CreateBookingAsync(BookingDraft draft, CancellationToken ct);

    /// <summary>Throws BookingSystemEntityNotFoundException for an unknown code.</summary>
    Task CancelBookingAsync(string code, CancellationToken ct);

    /// <summary>Null when the product/option is unknown; empty list when the
    /// range simply has no timeslots.</summary>
    Task<IReadOnlyList<AvailabilityTimeslot>?> GetAvailabilityAsync(
        int productExternalId, int optionExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task UpdateAvailabilityAsync(AvailabilityUpdate update, CancellationToken ct);

    Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(CancellationToken ct);

    Task<IReadOnlyList<ProductOptionSnapshot>> GetProductOptionsAsync(
        int productExternalId, CancellationToken ct);
}
