using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class SyncConcurrencyTests(PostgresFixture fixture)
{
    private static readonly CodeFormat Format = CodeFormat.Create("^MT-[A-Z0-9]{8}$");
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Two_workers_cannot_both_claim_the_same_pending_booking()
    {
        int id;
        await using (var seed = fixture.CreateContext())
        {
            var booking = Booking.Receive(
                BookingCode.Create("MT-RACE0001", Format), "s", BookingType.Api,
                BookingStatus.Pending, 10m, SyncDirection.Inbound, At);
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
            id = booking.Id;
        }

        await using var firstWorker = fixture.CreateContext();
        await using var secondWorker = fixture.CreateContext();
        var first = await firstWorker.Bookings.SingleAsync(b => b.Id == id);
        var second = await secondWorker.Bookings.SingleAsync(b => b.Id == id);

        first.ClaimForSync();
        second.ClaimForSync();

        await firstWorker.SaveChangesAsync();

        // The loser's snapshot is stale (xmin changed); it must not silently
        // double-claim. It reloads, sees Processing, and moves on.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondWorker.SaveChangesAsync());

        await using var verify = fixture.CreateContext();
        var final = await verify.Bookings.SingleAsync(b => b.Id == id);
        Assert.Equal(SyncStatus.Processing, final.Sync.Status);
    }
}
