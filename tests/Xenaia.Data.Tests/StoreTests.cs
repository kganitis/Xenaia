using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xenaia.Data.PostgreSql;
using Xenaia.Domain.Bookings.Availabilities;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Tests;

/// <summary>
/// Behaviour of the EF store implementations against a real Postgres schema:
/// round trips, the atomic claim, startup recovery, and the availability
/// unique index. Each test uses distinct external ids/codes so the shared
/// container needs no per-test cleanup.
/// </summary>
[Collection("postgres")]
public class StoreTests(PostgresFixture fixture)
{
    private static readonly CodeFormat BookingFormat = CodeFormat.Create("^MT-[A-Z0-9]{8}$");
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Outbound_request_round_trips_kind_payload_and_sync_state()
    {
        var request = OutboundBookingRequest.ForCreate("""{"draft":"meridian"}""");
        request.ClaimForSync();
        request.MarkSyncFailed("provider unreachable", At);

        int id;
        await using (var write = fixture.CreateContext())
        {
            var store = new EfOutboundBookingRequestStore(write);
            await store.AddAsync(request, default);
            await store.SaveChangesAsync(default);
            id = request.Id;
        }

        await using var read = fixture.CreateContext();
        var loaded = await read.OutboundBookingRequests.SingleAsync(r => r.Id == id);
        Assert.Equal(OutboundBookingKind.Create, loaded.Kind);
        Assert.Equal("""{"draft":"meridian"}""", loaded.Payload);
        Assert.Equal(SyncStatus.Failed, loaded.Sync.Status);
        Assert.Equal("provider unreachable", loaded.Sync.Error);
    }

    [Fact]
    public async Task Outbound_request_get_by_id_loads_the_row_and_is_null_for_an_unknown_id()
    {
        var request = OutboundBookingRequest.ForCancel("MT-GETBYID1");

        int id;
        await using (var write = fixture.CreateContext())
        {
            var store = new EfOutboundBookingRequestStore(write);
            await store.AddAsync(request, default);
            await store.SaveChangesAsync(default);
            id = request.Id;
        }

        await using var read = fixture.CreateContext();
        var store2 = new EfOutboundBookingRequestStore(read);

        var loaded = await store2.GetByIdAsync(id, default);
        Assert.NotNull(loaded);
        Assert.Equal(OutboundBookingKind.Cancel, loaded!.Kind);
        Assert.Equal("MT-GETBYID1", loaded.Payload);

        Assert.Null(await store2.GetByIdAsync(id + 100_000, default));
    }

    [Fact]
    public async Task Checkpoint_is_null_when_missing_then_set_then_overwritten()
    {
        const string name = "sync.checkpoint.missing-set-overwrite";
        var first = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 7, 15, 12, 30, 0, TimeSpan.Zero);

        await using var context = fixture.CreateContext();
        var store = new EfSyncCheckpointStore(context);

        Assert.Null(await store.GetAsync(name, default));

        await store.SetAsync(name, first, default);
        Assert.Equal(first, await store.GetAsync(name, default));

        await store.SetAsync(name, second, default);
        Assert.Equal(second, await store.GetAsync(name, default));
    }

    [Fact]
    public async Task Duplicate_availability_key_violates_the_unique_index_with_23505()
    {
        var slot = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        await using var context = fixture.CreateContext();
        context.Availabilities.Add(Availability.ForTimeslot(9001, 1, slot));
        await context.SaveChangesAsync();

        await using var second = fixture.CreateContext();
        second.Availabilities.Add(Availability.ForTimeslot(9001, 1, slot));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Equal("23505", (ex.InnerException as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task Availability_store_maps_a_duplicate_insert_to_a_domain_exception()
    {
        var slot = new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);
        await using (var seed = fixture.CreateContext())
        {
            seed.Availabilities.Add(Availability.ForTimeslot(9004, 1, slot));
            await seed.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var store = new EfAvailabilityStore(context, new PostgresDbExceptionInterpreter());
        await store.AddAsync(Availability.ForTimeslot(9004, 1, slot), default);

        // The provider interpreter classifies the Npgsql 23505 and the store
        // rethrows it as the domain exception the patch service retries on.
        var ex = await Assert.ThrowsAsync<DuplicateAvailabilityException>(
            () => store.SaveChangesAsync(default));
        Assert.Contains(new AvailabilityKey(9004, 1, slot), ex.ConflictingKeys);
    }

    [Fact]
    public async Task Availability_try_claim_succeeds_once_then_fails()
    {
        var slot = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        int id;
        await using (var seed = fixture.CreateContext())
        {
            var availability = Availability.ForTimeslot(9002, 1, slot);
            seed.Availabilities.Add(availability);
            await seed.SaveChangesAsync();
            id = availability.Id;
        }

        await using var context = fixture.CreateContext();
        var store = new EfAvailabilityStore(context, new PostgresDbExceptionInterpreter());

        Assert.True(await store.TryClaimAsync(id, default));
        Assert.False(await store.TryClaimAsync(id, default));

        await using var verify = fixture.CreateContext();
        var claimed = await verify.Availabilities.SingleAsync(a => a.Id == id);
        Assert.Equal(SyncStatus.Processing, claimed.Sync.Status);
    }

    [Fact]
    public async Task Reset_processing_flips_a_processing_availability_back_to_pending()
    {
        var slot = new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);
        int id;
        await using (var seed = fixture.CreateContext())
        {
            var availability = Availability.ForTimeslot(9003, 1, slot);
            availability.ClaimForSync();
            seed.Availabilities.Add(availability);
            await seed.SaveChangesAsync();
            id = availability.Id;
        }

        await using var context = fixture.CreateContext();
        var store = new EfAvailabilityStore(context, new PostgresDbExceptionInterpreter());

        var reset = await store.ResetProcessingAsync(default);
        Assert.True(reset >= 1);

        await using var verify = fixture.CreateContext();
        var recovered = await verify.Availabilities.SingleAsync(a => a.Id == id);
        Assert.Equal(SyncStatus.Pending, recovered.Sync.Status);
    }

    [Fact]
    public async Task Booking_get_by_code_returns_the_aggregate_with_its_children()
    {
        var booking = Booking.Receive(
            BookingCode.Create("MT-CHILD001", BookingFormat), "secret", BookingType.Api,
            BookingStatus.Pending, 200m, SyncDirection.Inbound, At);
        booking.AddItem(6001, 42, 7, "adult", At.AddDays(2), 120m);
        booking.AddExtra(6101, 7, "lunch", "Picnic lunch", At.AddDays(2), 1, 15m);
        booking.RecordPayment(6201, 200m, "card", PaymentStatus.Captured, At);
        booking.ApplyGiftCard("MTGIFT-CHILD", 20m);
        booking.DequeueDomainEvents();

        await using (var write = fixture.CreateContext())
        {
            var store = new EfBookingStore(write);
            await store.AddAsync(booking, default);
            await store.SaveChangesAsync(default);
        }

        await using var read = fixture.CreateContext();
        var loaded = await new EfBookingStore(read).GetByCodeAsync("MT-CHILD001", default);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Items);
        Assert.Single(loaded.Extras);
        Assert.Single(loaded.Payments);
        Assert.Single(loaded.GiftCards);
    }

    [Fact]
    public async Task Booking_get_by_code_returns_null_when_absent()
    {
        await using var read = fixture.CreateContext();
        Assert.Null(await new EfBookingStore(read).GetByCodeAsync("MT-NOSUCH01", default));
    }

    [Fact]
    public async Task Catalog_get_participant_types_resolves_by_external_ids()
    {
        int optionInternalId;
        await using (var write = fixture.CreateContext())
        {
            var product = Product.Define(7042, "Cooking Class");
            product.AddOption(7007, "Evening session");
            var store = new EfCatalogStore(write);
            await store.AddAsync(product, default);
            await store.SaveChangesAsync(default);
            optionInternalId = product.Options.Single().Id;
        }

        await using (var write = fixture.CreateContext())
        {
            var store = new EfCatalogStore(write);
            await store.AddParticipantTypeAsync(
                ParticipantType.Define(optionInternalId, "adult", "Adult"), default);
            await store.AddParticipantTypeAsync(
                ParticipantType.Define(optionInternalId, "child", "Child"), default);
            await store.SaveChangesAsync(default);
        }

        await using var read = fixture.CreateContext();
        var types = await new EfCatalogStore(read)
            .GetParticipantTypesAsync(7042, 7007, default);

        Assert.Equal(2, types.Count);
        Assert.Contains(types, t => t.Alias == "adult");
        Assert.Contains(types, t => t.Alias == "child");
    }

    [Fact]
    public async Task Catalog_get_participant_types_is_empty_for_unknown_option()
    {
        await using var read = fixture.CreateContext();
        var types = await new EfCatalogStore(read)
            .GetParticipantTypesAsync(999999, 999999, default);
        Assert.Empty(types);
    }

    [Fact]
    public async Task Product_without_a_code_round_trips_with_a_null_code()
    {
        var product = Product.Define(7043, "Guided Walk");

        await using (var write = fixture.CreateContext())
        {
            write.Products.Add(product);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var loaded = await read.Products.SingleAsync(p => p.ExternalId == 7043);
        Assert.Null(loaded.Code);
    }

    [Fact]
    public async Task Deleting_a_booking_cascades_to_its_children()
    {
        var booking = Booking.Receive(
            BookingCode.Create("MT-CASCADE1", BookingFormat), "secret", BookingType.Api,
            BookingStatus.Pending, 50m, SyncDirection.Inbound, At);
        booking.AddItem(6301, 42, 7, "adult", At.AddDays(1), 50m);
        booking.RecordPayment(6302, 50m, "card", PaymentStatus.Captured, At);
        booking.DequeueDomainEvents();

        int id;
        await using (var write = fixture.CreateContext())
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
            id = booking.Id;
        }

        await using (var delete = fixture.CreateContext())
        {
            var loaded = await delete.Bookings
                .Include(b => b.Items)
                .Include(b => b.Payments)
                .SingleAsync(b => b.Id == id);
            delete.Bookings.Remove(loaded);
            await delete.SaveChangesAsync();
        }

        await using var verify = fixture.CreateContext();
        var items = await verify.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM booking_item WHERE booking_id = {id}")
            .SingleAsync();
        var payments = await verify.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM booking_payment WHERE booking_id = {id}")
            .SingleAsync();
        Assert.Equal(0, items);
        Assert.Equal(0, payments);
    }
}
