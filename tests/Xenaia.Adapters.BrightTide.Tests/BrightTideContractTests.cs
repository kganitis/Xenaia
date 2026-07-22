using Xenaia.Adapters.BrightTide;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.PortContracts.BookingSystem;

namespace Xenaia.Adapters.BrightTide.Tests;

/// <summary>
/// The reusable IBookingSystemProvider contract, run against the real adapter
/// over a stateful fake BrightTide vendor. What the in-memory provider
/// promises, BrightTide must promise too, proven through real HTTP-shaped
/// round-trips.
/// </summary>
public class BrightTideContractTests : BookingSystemProviderContractTests
{
    private BrightTideFakeVendorHandler? _vendor;

    protected override Task<IBookingSystemProvider> CreateProviderAsync()
    {
        _vendor = new BrightTideFakeVendorHandler();
        var http = new HttpClient(_vendor) { BaseAddress = new Uri("https://brighttide.example/") };
        http.DefaultRequestHeaders.Add("API-Key", "test-key");
        return Task.FromResult<IBookingSystemProvider>(new BrightTideClient(http));
    }

    protected override Task SeedProductAsync(
        ProductSnapshot product, params ProductOptionSnapshot[] options)
    {
        _vendor!.SeedProduct(product, options);
        return Task.CompletedTask;
    }

    protected override Task SeedBookingAsync(BookingSnapshot booking)
    {
        _vendor!.SeedBooking(booking);
        return Task.CompletedTask;
    }

    protected override Task SeedAvailabilityAsync(
        int productExternalId, int optionExternalId, params AvailabilityTimeslot[] slots)
    {
        _vendor!.SeedAvailability(productExternalId, optionExternalId, slots);
        return Task.CompletedTask;
    }
}
