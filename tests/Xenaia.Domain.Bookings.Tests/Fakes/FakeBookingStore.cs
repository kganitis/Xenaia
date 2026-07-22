using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Domain.Bookings.Tests.Fakes;

/// <summary>In-memory IBookingStore for BookingIngestService tests: an
/// in-memory dictionary keyed by booking code, SaveChangesAsync a counter.</summary>
internal sealed class FakeBookingStore : IBookingStore
{
    private readonly Dictionary<string, Booking> _byCode = [];

    public int SaveChangesCount { get; private set; }

    public void Seed(Booking booking) => _byCode[booking.Code.Value] = booking;

    public Task<Booking?> GetByCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(_byCode.GetValueOrDefault(code));

    public Task AddAsync(Booking booking, CancellationToken ct)
    {
        _byCode[booking.Code.Value] = booking;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}
