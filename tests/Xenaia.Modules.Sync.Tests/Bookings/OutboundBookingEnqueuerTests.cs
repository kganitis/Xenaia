using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Bookings;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.Fakes;

namespace Xenaia.Modules.Sync.Tests.Bookings;

public class OutboundBookingEnqueuerTests
{
    private const int ProductId = 100;
    private const int OptionId = 7;
    private static readonly DateTimeOffset ActivityAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly CodeFormats _formats = new(Options.Create(new BookingsFormatOptions
    {
        BookingCodePattern = "^MT-[0-9]{4,}$",
        ProductCodePattern = "^MTP-[A-Z0-9]{4}$",
    }));

    [Fact]
    public async Task Enqueue_create_persists_pending_request_with_draft_json_and_wakes_the_channel()
    {
        var store = new FakeOutboundBookingRequestStore();
        var catalog = new FakeCatalogStore();
        catalog.SeedProduct(ProductWithOption());
        var channel = new BookingChannel(10);
        var sut = CreateSut(store, new FakeBookingStore(), catalog, channel);

        var draft = DraftFor(ProductId, OptionId);
        var id = await sut.EnqueueCreateAsync(draft, CancellationToken.None);

        var request = Assert.Single(store.All);
        Assert.Equal(id, request.Id);
        Assert.Equal(OutboundBookingKind.Create, request.Kind);
        Assert.Equal(SyncStatus.Pending, request.Sync.Status);

        var roundTripped = JsonSerializer.Deserialize<BookingDraft>(request.Payload, Web)!;
        Assert.Equal(ProductId, roundTripped.Items.Single().ProductExternalId);
        Assert.Equal(OptionId, roundTripped.Items.Single().OptionExternalId);

        Assert.True(channel.Reader.TryRead(out var woken));
        Assert.Equal(id, woken);
    }

    [Fact]
    public async Task Enqueue_create_with_unknown_product_throws_and_persists_nothing()
    {
        var store = new FakeOutboundBookingRequestStore();
        var catalog = new FakeCatalogStore();
        catalog.SeedProduct(ProductWithOption());
        var channel = new BookingChannel(10);
        var sut = CreateSut(store, new FakeBookingStore(), catalog, channel);

        var draft = DraftFor(productExternalId: 999, OptionId); // product not in catalog

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.EnqueueCreateAsync(draft, CancellationToken.None));

        Assert.Empty(store.All);
        Assert.Equal(0, store.SaveChangesCallCount);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Enqueue_create_with_unknown_option_throws_and_persists_nothing()
    {
        var store = new FakeOutboundBookingRequestStore();
        var catalog = new FakeCatalogStore();
        catalog.SeedProduct(ProductWithOption());
        var sut = CreateSut(store, new FakeBookingStore(), catalog, new BookingChannel(10));

        var draft = DraftFor(ProductId, optionExternalId: 42); // option not on the product

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.EnqueueCreateAsync(draft, CancellationToken.None));

        Assert.Empty(store.All);
        Assert.Equal(0, store.SaveChangesCallCount);
    }

    [Fact]
    public async Task Enqueue_cancel_for_an_active_booking_persists_a_pending_cancel_request()
    {
        var store = new FakeOutboundBookingRequestStore();
        var bookingStore = new FakeBookingStore();
        bookingStore.Seed(ActiveBooking("MT-5000"));
        var channel = new BookingChannel(10);
        var sut = CreateSut(store, bookingStore, new FakeCatalogStore(), channel);

        var id = await sut.EnqueueCancelAsync("MT-5000", CancellationToken.None);

        var request = Assert.Single(store.All);
        Assert.Equal(id, request.Id);
        Assert.Equal(OutboundBookingKind.Cancel, request.Kind);
        Assert.Equal("MT-5000", request.Payload);
        Assert.Equal(SyncStatus.Pending, request.Sync.Status);
        Assert.True(channel.Reader.TryRead(out var woken));
        Assert.Equal(id, woken);
    }

    [Fact]
    public async Task Enqueue_cancel_for_an_unknown_code_throws_and_persists_nothing()
    {
        var store = new FakeOutboundBookingRequestStore();
        var sut = CreateSut(store, new FakeBookingStore(), new FakeCatalogStore(), new BookingChannel(10));

        await Assert.ThrowsAsync<UnknownBookingException>(
            () => sut.EnqueueCancelAsync("MT-0000", CancellationToken.None));

        Assert.Empty(store.All);
        Assert.Equal(0, store.SaveChangesCallCount);
    }

    [Fact]
    public async Task Enqueue_cancel_for_an_already_cancelled_booking_throws_and_persists_nothing()
    {
        var store = new FakeOutboundBookingRequestStore();
        var bookingStore = new FakeBookingStore();
        var booking = ActiveBooking("MT-6000");
        booking.Cancel(ActivityAt);
        bookingStore.Seed(booking);
        var sut = CreateSut(store, bookingStore, new FakeCatalogStore(), new BookingChannel(10));

        await Assert.ThrowsAsync<BookingAlreadyCancelledException>(
            () => sut.EnqueueCancelAsync("MT-6000", CancellationToken.None));

        Assert.Empty(store.All);
        Assert.Equal(0, store.SaveChangesCallCount);
    }

    private static Product ProductWithOption()
    {
        var product = Product.Define(ProductId, "Sunset Kayak Tour");
        product.AddOption(OptionId, "Standard");
        return product;
    }

    private static BookingDraft DraftFor(int productExternalId, int optionExternalId) => new()
    {
        Type = BookingType.Api,
        LeadContactName = "Ada Coastline",
        Items = [new BookingDraftItem(productExternalId, optionExternalId, "adult", ActivityAt, 49.50m)],
    };

    private Booking ActiveBooking(string code) => Booking.Receive(
        BookingCode.Create(code, _formats.BookingCode), $"SEC-{code}",
        BookingType.Api, BookingStatus.Pending, 49.50m, SyncDirection.Outbound, ActivityAt);

    private static OutboundBookingEnqueuer CreateSut(
        FakeOutboundBookingRequestStore store,
        FakeBookingStore bookingStore,
        FakeCatalogStore catalog,
        BookingChannel channel)
        => new(store, bookingStore, catalog, channel, NullLogger<OutboundBookingEnqueuer>.Instance);
}
