using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>Booking system provider whose UpdateAvailabilityAsync throws the
/// supplied exception on every call, counting invocations so the pusher's
/// retry behaviour can be asserted. Every other member is unused by the
/// availability pusher and left unsupported.</summary>
internal sealed class ThrowingBookingSystemProvider(Exception toThrow) : IBookingSystemProvider
{
    public int UpdateCallCount { get; private set; }

    public Task UpdateAvailabilityAsync(AvailabilityUpdate update, CancellationToken ct)
    {
        UpdateCallCount++;
        throw toThrow;
    }

    public Task<IReadOnlyList<BookingSnapshot>> GetBookingsAsync(BookingQuery query, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<BookingSnapshot?> GetBookingByCodeAsync(string code, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<BookingSnapshot> CreateBookingAsync(BookingDraft draft, CancellationToken ct)
        => throw new NotSupportedException();

    public Task CancelBookingAsync(string code, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<AvailabilityTimeslot>?> GetAvailabilityAsync(
        int productExternalId, int optionExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ProductOptionSnapshot>> GetProductOptionsAsync(
        int productExternalId, CancellationToken ct)
        => throw new NotSupportedException();
}
