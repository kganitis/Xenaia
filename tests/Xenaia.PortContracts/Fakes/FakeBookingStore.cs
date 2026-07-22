using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.PortContracts.Fakes;

/// <summary>In-memory IBookingStore shared by BookingIngestService tests and
/// BookingInboundSweep tests: an in-memory dictionary keyed by booking code,
/// SaveChangesAsync a counter. A single shared copy: both consumers need the
/// same shape, so it lives here rather than duplicated per test project.</summary>
public sealed class FakeBookingStore : IBookingStore
{
    private readonly Dictionary<string, Booking> _byCode = [];

    public int SaveChangesCount { get; private set; }

    public IReadOnlyCollection<Booking> Bookings => _byCode.Values;

    /// <summary>When set, the next GetByCodeAsync call cancels this source
    /// and throws OperationCanceledException, simulating a host-shutdown
    /// cancellation arriving mid-ingest.</summary>
    public CancellationTokenSource? CancelDuringGetByCode { get; set; }

    public void Seed(Booking booking) => _byCode[booking.Code.Value] = booking;

    public Task<Booking?> GetByCodeAsync(string code, CancellationToken ct)
    {
        if (CancelDuringGetByCode is { } cts)
        {
            cts.Cancel();
            throw new OperationCanceledException(ct);
        }
        return Task.FromResult(_byCode.GetValueOrDefault(code));
    }

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
