using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;
using Xunit;

namespace Xenaia.PortContracts.BookingSystem;

/// <summary>
/// Reusable behavioral contract for IBookingSystemProvider. Any adapter must
/// pass this suite; inherit it, implement the harness hooks, and the port's
/// semantics are asserted for free (design section 8, port contract tests).
/// </summary>
public abstract class BookingSystemProviderContractTests
{
    /// <summary>Creates a provider with an empty backing store.</summary>
    protected abstract Task<IBookingSystemProvider> CreateProviderAsync();

    /// <summary>Seeds a product and its options directly into the backing
    /// store, bypassing the port (products are read-only through it).</summary>
    protected abstract Task SeedProductAsync(
        ProductSnapshot product, params ProductOptionSnapshot[] options);

    /// <summary>Seeds a booking directly into the backing store, with full
    /// control over fields (such as UpdatedAtExternal) the port itself would
    /// not let a test pin down deterministically.</summary>
    protected abstract Task SeedBookingAsync(BookingSnapshot booking);

    /// <summary>Seeds availability timeslots for a product/option directly
    /// into the backing store. Calling with no slots still registers the
    /// combination as known but empty.</summary>
    protected abstract Task SeedAvailabilityAsync(
        int productExternalId, int optionExternalId, params AvailabilityTimeslot[] slots);

    private static readonly DateTimeOffset ActivityAt =
        new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static BookingSnapshot Booking(
        string code,
        BookingStatus status = BookingStatus.Pending,
        DateTimeOffset? updatedAtExternal = null) => new()
    {
        Code = code,
        SecretCode = $"SEC-{code}",
        Type = BookingType.Api,
        Status = status,
        FinalPrice = 42m,
        CreatedAtExternal = updatedAtExternal,
        UpdatedAtExternal = updatedAtExternal,
        Items = [new BookingItemSnapshot(1, 100, 200, "adult", ActivityAt, 42m)],
    };

    private static BookingDraft Draft() => new()
    {
        Type = BookingType.Api,
        Email = "guest@example.com",
        Items = [new BookingDraftItem(100, 200, "adult", ActivityAt, 42m)],
    };

    [Fact]
    public async Task Getting_an_unknown_code_returns_null()
    {
        var provider = await CreateProviderAsync();

        var booking = await provider.GetBookingByCodeAsync("MT-MISSING", CancellationToken.None);

        Assert.Null(booking);
    }

    [Fact]
    public async Task Seeded_bookings_round_trip_and_honor_the_updated_from_filter()
    {
        var provider = await CreateProviderAsync();
        await SeedBookingAsync(Booking("MT-1",
            updatedAtExternal: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        await SeedBookingAsync(Booking("MT-2",
            updatedAtExternal: new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero)));

        var all = await provider.GetBookingsAsync(new BookingQuery(), CancellationToken.None);
        Assert.Equal(["MT-1", "MT-2"], all.Select(b => b.Code).OrderBy(c => c).ToArray());

        var recent = await provider.GetBookingsAsync(
            new BookingQuery { UpdatedFrom = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero) },
            CancellationToken.None);
        Assert.Equal(["MT-2"], recent.Select(b => b.Code).ToArray());
    }

    [Fact]
    public async Task Creating_a_booking_assigns_a_code_and_the_booking_becomes_readable()
    {
        var provider = await CreateProviderAsync();

        var created = await provider.CreateBookingAsync(Draft(), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(created.Code));

        var reread = await provider.GetBookingByCodeAsync(created.Code, CancellationToken.None);
        Assert.NotNull(reread);
        Assert.Equal(created.Code, reread!.Code);
    }

    [Fact]
    public async Task Cancelling_an_unknown_code_throws_not_found()
    {
        var provider = await CreateProviderAsync();

        await Assert.ThrowsAsync<BookingSystemEntityNotFoundException>(() =>
            provider.CancelBookingAsync("MT-MISSING", CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_then_reading_shows_cancelled()
    {
        var provider = await CreateProviderAsync();
        var created = await provider.CreateBookingAsync(Draft(), CancellationToken.None);

        await provider.CancelBookingAsync(created.Code, CancellationToken.None);

        var reread = await provider.GetBookingByCodeAsync(created.Code, CancellationToken.None);
        Assert.Equal(BookingStatus.Cancelled, reread!.Status);
    }

    [Fact]
    public async Task Availability_for_an_unknown_product_or_option_returns_null()
    {
        var provider = await CreateProviderAsync();

        var availability = await provider.GetAvailabilityAsync(
            999, 999,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Null(availability);
    }

    [Fact]
    public async Task Availability_for_a_known_product_with_an_empty_range_returns_an_empty_list()
    {
        var provider = await CreateProviderAsync();
        await SeedAvailabilityAsync(100, 200);

        var availability = await provider.GetAvailabilityAsync(
            100, 200,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.NotNull(availability);
        Assert.Empty(availability!);
    }

    [Fact]
    public async Task Availability_returns_seeded_slots()
    {
        var provider = await CreateProviderAsync();
        var slot = new AvailabilityTimeslot(new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero), 4);
        await SeedAvailabilityAsync(100, 200, slot);

        var availability = await provider.GetAvailabilityAsync(
            100, 200,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal([slot], availability);
    }

    [Fact]
    public async Task Updating_availability_is_reflected_in_a_subsequent_read()
    {
        var provider = await CreateProviderAsync();
        var at = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        await SeedAvailabilityAsync(100, 200, new AvailabilityTimeslot(at, 2));

        await provider.UpdateAvailabilityAsync(new AvailabilityUpdate(
            ProductExternalId: 100,
            OptionExternalId: 200,
            From: at,
            To: at,
            Times: null,
            Vacancies: 7,
            StopSales: null,
            ParticipantTypeAliases: ["adult"]), CancellationToken.None);

        var availability = await provider.GetAvailabilityAsync(
            100, 200,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(7, Assert.Single(availability!).Vacancies);
    }

    [Fact]
    public async Task A_signal_only_update_on_an_unknown_product_or_option_leaves_it_unknown()
    {
        var provider = await CreateProviderAsync();
        var at = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

        await provider.UpdateAvailabilityAsync(new AvailabilityUpdate(
            ProductExternalId: 999,
            OptionExternalId: 999,
            From: at,
            To: at,
            Times: null,
            Vacancies: null,
            StopSales: true,
            ParticipantTypeAliases: ["adult"]), CancellationToken.None);

        var availability = await provider.GetAvailabilityAsync(
            999, 999,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Null(availability);
    }

    [Fact]
    public async Task Products_and_options_round_trip_with_participant_types()
    {
        var provider = await CreateProviderAsync();
        var product = new ProductSnapshot(100, "Kayak Tour", 1);
        var option = new ProductOptionSnapshot(200, "Half Day",
            [new ParticipantTypeSnapshot("adult", "Adult"), new ParticipantTypeSnapshot("child", "Child")]);
        await SeedProductAsync(product, option);

        var products = await provider.GetProductsAsync(CancellationToken.None);
        Assert.Contains(products, p => p.ExternalId == 100 && p.Title == "Kayak Tour");

        var options = await provider.GetProductOptionsAsync(100, CancellationToken.None);
        var roundTripped = Assert.Single(options);
        Assert.Equal("Half Day", roundTripped.Title);
        Assert.Equal(["adult", "child"], roundTripped.ParticipantTypes.Select(p => p.Alias).ToArray());
    }
}
