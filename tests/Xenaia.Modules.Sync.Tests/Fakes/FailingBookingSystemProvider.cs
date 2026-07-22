using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>Booking system provider whose CreateBookingAsync and
/// CancelBookingAsync throw the supplied exception on every call, counting
/// invocations so the pusher's retry behaviour can be asserted (a not-found is
/// permanent and must fire exactly one call; a retryable fault must exhaust to
/// the attempt count). Every other member is unused by the booking pusher and
/// left unsupported.</summary>
internal sealed class FailingBookingSystemProvider(Exception toThrow) : IBookingSystemProvider
{
    public int CreateCallCount { get; private set; }

    public int CancelCallCount { get; private set; }

    public Task<BookingSnapshot> CreateBookingAsync(BookingDraft draft, CancellationToken ct)
    {
        CreateCallCount++;
        throw toThrow;
    }

    public Task CancelBookingAsync(string code, CancellationToken ct)
    {
        CancelCallCount++;
        throw toThrow;
    }

    public Task<IReadOnlyList<BookingSnapshot>> GetBookingsAsync(BookingQuery query, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<BookingSnapshot?> GetBookingByCodeAsync(string code, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<AvailabilityTimeslot>?> GetAvailabilityAsync(
        int productExternalId, int optionExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        => throw new NotSupportedException();

    public Task UpdateAvailabilityAsync(AvailabilityUpdate update, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ProductOptionSnapshot>> GetProductOptionsAsync(
        int productExternalId, CancellationToken ct)
        => throw new NotSupportedException();
}
