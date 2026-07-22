using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Data;

/// <summary>EF-backed repository for the Booking aggregate.</summary>
public sealed class EfBookingStore(XenaiaDbContext context) : IBookingStore
{
    public async Task<Booking?> GetByCodeAsync(string code, CancellationToken ct)
    {
        var typed = BookingCode.FromTrusted(code);
        return await context.Bookings
            .Include(b => b.Items)
            .Include(b => b.Extras)
            .Include(b => b.Payments)
            .Include(b => b.GiftCards)
            .SingleOrDefaultAsync(b => b.Code == typed, ct);
    }

    public Task AddAsync(Booking booking, CancellationToken ct)
    {
        context.Bookings.Add(booking);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
