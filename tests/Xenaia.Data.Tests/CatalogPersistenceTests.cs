using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Availabilities;
using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Channels;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class CatalogPersistenceTests(PostgresFixture fixture)
{
    private static readonly CodeFormat Format = CodeFormat.Create("^MTP-[A-Z0-9]{4}$");
    private static readonly DateTimeOffset Slot = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Product_round_trips_with_options_extra_links_and_code()
    {
        var product = Product.Define(42, "Sunset Kayak Tour", categoryId: 3);
        product.AssignCode(ProductCode.Create("MTP-K4Y2", Format));
        product.AddOption(7, "Two-seat kayak").LinkExtra(31);
        product.AddOption(8, "Single kayak");

        await using (var write = fixture.CreateContext())
        {
            write.Products.Add(product);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var loaded = await read.Products
            .Include(p => p.Options)
            .ThenInclude(o => o.ExtraLinks)
            .SingleAsync(p => p.ExternalId == 42);

        Assert.Equal("MTP-K4Y2", loaded.Code!.Value);
        Assert.Equal(2, loaded.Options.Count);
        Assert.Equal(31, loaded.Options.Single(o => o.ExternalId == 7).ExtraLinks.Single().ExtraId);
    }

    [Fact]
    public async Task Channel_availability_and_catalog_entities_round_trip()
    {
        var availability = Availability.ForTimeslot(42, 7, Slot);
        availability.SetVacancies(12);

        await using (var write = fixture.CreateContext())
        {
            write.Channels.Add(Channel.Define("PARTNER-A", "Partner A Storefront"));
            write.Availabilities.Add(availability);
            write.Categories.Add(Category.Define(3, "Water Activities"));
            write.Extras.Add(Extra.Define("lunch", "Picnic lunch", price: 12.50m));
            write.ParticipantTypes.Add(ParticipantType.Define(7, "adult", "Adult"));
            write.PaymentTypes.Add(PaymentType.Define("card", "Credit card"));
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();

        Assert.True((await read.Channels.SingleAsync(c => c.Code == "PARTNER-A")).IsActive);
        var loadedAvailability = await read.Availabilities.SingleAsync(
            a => a.ExternalProductId == 42 && a.ExternalOptionId == 7 && a.TimeslotAt == Slot);
        Assert.Equal(12, loadedAvailability.Vacancies);
        Assert.Null(loadedAvailability.StopSales);
        Assert.Equal(SyncStatus.Pending, loadedAvailability.Sync.Status);
        Assert.Equal(12.50m, (await read.Extras.SingleAsync(e => e.Alias == "lunch")).Price);
        Assert.NotNull(await read.Categories.SingleAsync(c => c.ExternalId == 3));
        Assert.NotNull(await read.ParticipantTypes.SingleAsync(p => p.Alias == "adult"));
        Assert.NotNull(await read.PaymentTypes.SingleAsync(p => p.Code == "card"));
    }

    [Fact]
    public async Task Duplicate_timeslots_are_rejected_by_the_database()
    {
        await using var context = fixture.CreateContext();
        context.Availabilities.Add(Availability.ForTimeslot(142, 17, Slot));
        await context.SaveChangesAsync();

        await using var second = fixture.CreateContext();
        second.Availabilities.Add(Availability.ForTimeslot(142, 17, Slot));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
