using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Modules.Sync.Tests.Fakes;

/// <summary>In-memory IBookingStore for BookingInboundSweep tests: an
/// in-memory dictionary keyed by booking code, SaveChangesAsync a counter.
/// Mirrors Xenaia.Domain.Bookings.Tests.Fakes.FakeBookingStore, kept as a
/// separate copy since each test project owns its own fakes.</summary>
internal sealed class FakeBookingStore : IBookingStore
{
    private readonly Dictionary<string, Booking> _byCode = [];

    public int SaveChangesCount { get; private set; }

    public IReadOnlyCollection<Booking> Bookings => _byCode.Values;

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
