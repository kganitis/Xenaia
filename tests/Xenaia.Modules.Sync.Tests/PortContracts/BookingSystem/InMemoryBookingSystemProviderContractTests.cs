using Xenaia.Domain.Bookings.Providers;
using Xenaia.PortContracts.BookingSystem;

namespace Xenaia.Modules.Sync.Tests.PortContracts.BookingSystem;

public class InMemoryBookingSystemProviderContractTests : BookingSystemProviderContractTests
{
    private InMemoryBookingSystemProvider? _provider;

    protected override Task<IBookingSystemProvider> CreateProviderAsync()
    {
        _provider = new InMemoryBookingSystemProvider();
        return Task.FromResult<IBookingSystemProvider>(_provider);
    }

    protected override Task SeedProductAsync(
        ProductSnapshot product, params ProductOptionSnapshot[] options)
    {
        _provider!.SeedProduct(product, options);
        return Task.CompletedTask;
    }

    protected override Task SeedBookingAsync(BookingSnapshot booking)
    {
        _provider!.SeedBooking(booking);
        return Task.CompletedTask;
    }

    protected override Task SeedAvailabilityAsync(
        int productExternalId, int optionExternalId, params AvailabilityTimeslot[] slots)
    {
        _provider!.SeedAvailability(productExternalId, optionExternalId, slots);
        return Task.CompletedTask;
    }
}
