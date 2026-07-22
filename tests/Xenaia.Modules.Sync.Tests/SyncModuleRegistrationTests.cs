using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xenaia.Core;
using Xenaia.Domain.Bookings;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.Modules.Sync.Tests.Fakes;
using Xenaia.PortContracts.BookingSystem;
using Xenaia.PortContracts.Fakes;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests;

/// <summary>
/// Regression tests for the Sync module's DI wiring. AvailabilityFetchService
/// takes ISpreadsheetGateway as a required dependency, so it must be registered
/// only when a spreadsheet provider is configured: with brighttide but no
/// spreadsheet provider, a Development host (ValidateScopes + ValidateOnBuild)
/// would otherwise fail to build. Both cases here build the exact production
/// shape, with fakes standing in for the EF stores and the vendor adapter.
/// </summary>
public class SyncModuleRegistrationTests
{
    private static IServiceCollection BaseServices(bool spreadsheetConfigured)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenant:BusinessName"] = "Meridian Trails",
            ["Tenant:TimeZone"] = "America/New_York",
            ["Tenant:Locales:0"] = "en-US",
            ["Tenant:Bookings:BookingCodePattern"] = "^MT-[A-Z0-9]{8}$",
            ["Tenant:Bookings:ProductCodePattern"] = "^MTP-[A-Z0-9]{4}$",
            // A Sync key so the section exists (AddValidatedOptions requires it).
            ["Sync:Availability:MaxBatchSize"] = "1000",
        };
        if (spreadsheetConfigured)
        {
            // Required by the validator once a spreadsheet provider is present.
            settings["Sync:Availability:PatchSheetName"] = "Availability Patch";
            settings["Sync:Availability:GetSheetName"] = "Availability Get";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        // NullLogger via Abstractions (already referenced) avoids taking a
        // dependency on the full logging package just for the DI shape test.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddXenaiaCore(configuration);
        services.AddBookingsDomain(configuration);

        // Stand-ins for the EF stores and the BrightTide adapter, all scoped in
        // production; the point of these tests is the DI shape, not behavior.
        services.AddScoped<IAvailabilityStore, FakeAvailabilityStore>();
        services.AddScoped<ICatalogStore, FakeCatalogStore>();
        services.AddScoped<IBookingStore, FakeBookingStore>();
        services.AddScoped<IOutboundBookingRequestStore, FakeOutboundBookingRequestStore>();
        services.AddScoped<ISyncCheckpointStore, FakeSyncCheckpointStore>();
        services.AddScoped<IBookingSystemProvider, InMemoryBookingSystemProvider>();

        services.AddSyncModule(configuration, spreadsheetConfigured);
        return services;
    }

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

    [Fact]
    public void Builds_without_a_spreadsheet_provider_and_omits_the_fetch_service()
    {
        var services = BaseServices(spreadsheetConfigured: false);

        // ValidateOnBuild constructs every registered service once; the whole
        // point is that this does not throw when no ISpreadsheetGateway exists.
        using var provider = Build(services);

        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<AvailabilityFetchService>());
        Assert.NotNull(scope.ServiceProvider.GetService<AvailabilityPatchService>());
    }

    [Fact]
    public void Builds_with_a_spreadsheet_provider_and_resolves_the_fetch_service()
    {
        var services = BaseServices(spreadsheetConfigured: true);
        services.AddSingleton<ISpreadsheetGateway, InMemorySpreadsheetGateway>();

        using var provider = Build(services);

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AvailabilityFetchService>());
    }
}
