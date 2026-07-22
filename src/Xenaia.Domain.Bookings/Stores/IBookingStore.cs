using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Domain.Bookings.Stores;

/// <summary>Repository port for the Booking aggregate. Implemented in
/// Xenaia.Data as an EF-backed scoped service.</summary>
public interface IBookingStore
{
    Task<Booking?> GetByCodeAsync(string code, CancellationToken ct);   // tracked, children included
    Task AddAsync(Booking booking, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
