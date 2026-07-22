using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core;
using Xenaia.Domain.Bookings;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;
using Xenaia.PortContracts.BookingSystem;
using Xenaia.PortContracts.Fakes;
using Xenaia.PortContracts.Helpdesk;
using Xunit;

namespace Xenaia.Modules.Triage.Tests;

public class TriageModuleRegistrationTests : IDisposable
{
    private readonly string _packPath = Path.Combine(Path.GetTempPath(), $"pack-{Guid.NewGuid():N}.yaml");

    public void Dispose()
    {
        if (File.Exists(_packPath)) File.Delete(_packPath);
    }

    private void WritePack(string yaml) => File.WriteAllText(_packPath, yaml);

    private const string GoodPack = """
        version: 1
        defaults:
          unmatchedCategory: needs-human
        rules:
          - id: urgent-booking
            category: new-booking
            match: { subject: '^New Booking' }
            processor: booking-urgency
        """;

    private ServiceProvider BuildHost(string? packPath = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:BusinessName"] = "Meridian Trails",
            ["Tenant:TimeZone"] = "America/New_York",
            ["Tenant:Locales:0"] = "en-US",
            ["Tenant:Triage:RulePackPath"] = packPath ?? _packPath,
            ["Tenant:Triage:PollIntervalSeconds"] = "60",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXenaiaCore(configuration);
        services.AddTriageModule(configuration);
        return services.BuildServiceProvider();
    }

    private void AssertStartupFails(ServiceProvider provider, string fragment)
    {
        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<TriageOptions>>().Value);
        Assert.Contains(fragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_configuration_resolves_options_and_rule_pack()
    {
        WritePack(GoodPack);
        using var provider = BuildHost();

        var options = provider.GetRequiredService<IOptions<TriageOptions>>().Value;
        var pack = provider.GetRequiredService<IRulePackProvider>().Pack;

        Assert.Equal(_packPath, options.RulePackPath);
        Assert.Equal("needs-human", pack.UnmatchedCategory);
        Assert.Single(pack.Rules);
    }

    [Fact]
    public void Missing_rule_pack_file_fails_validation()
    {
        using var provider = BuildHost(packPath: "/nowhere/no-pack.yaml");

        AssertStartupFails(provider, "not found");
    }

    [Fact]
    public void Invalid_rule_pack_fails_validation()
    {
        WritePack("version: 9");
        using var provider = BuildHost();

        AssertStartupFails(provider, "version must be 1");
    }

    [Fact]
    public void Unregistered_processor_reference_fails_validation()
    {
        WritePack("""
            version: 1
            defaults:
              unmatchedCategory: needs-human
            rules:
              - id: a
                category: c
                match: { subject: p }
                processor: warp-drive
            """);
        using var provider = BuildHost();

        AssertStartupFails(provider, "warp-drive");
    }

    [Fact]
    public void Empty_date_time_formats_fail_validation()
    {
        WritePack(GoodPack);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:BusinessName"] = "Meridian Trails",
            ["Tenant:TimeZone"] = "America/New_York",
            ["Tenant:Locales:0"] = "en-US",
            ["Tenant:Triage:RulePackPath"] = _packPath,
            ["Tenant:Triage:Urgency:DateTimeFormats:0"] = "",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXenaiaCore(configuration);
        services.AddTriageModule(configuration);
        using var provider = services.BuildServiceProvider();

        AssertStartupFails(provider, "DateTimeFormats");
    }

    /// <summary>
    /// Regression test for the TriageOptionsValidator scope fix: with a
    /// booking system configured, BookingLookupProcessor (scoped, since its
    /// dependencies are EF/adapter-scoped services) joins the registered
    /// processors. Resolving IOptions&lt;TriageOptions&gt;.Value triggers
    /// TriageOptionsValidator, which must resolve ITicketProcessor
    /// instances to check names; doing that straight off the root provider
    /// throws under ValidateScopes. This is the exact production shape
    /// (host builds with Development's default ValidateScopes/ValidateOnBuild)
    /// the fix targets, so it must not throw here either.
    /// </summary>
    [Fact]
    public void Scoped_booking_lookup_processor_resolves_under_scope_validation()
    {
        WritePack(GoodPack);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:BusinessName"] = "Meridian Trails",
            ["Tenant:TimeZone"] = "America/New_York",
            ["Tenant:Locales:0"] = "en-US",
            ["Tenant:Triage:RulePackPath"] = _packPath,
            ["Tenant:Triage:PollIntervalSeconds"] = "60",
            ["Tenant:Bookings:BookingCodePattern"] = "^MT-[A-Z0-9]{8}$",
            ["Tenant:Bookings:ProductCodePattern"] = "^MTP-[A-Z0-9]{4}$",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXenaiaCore(configuration);
        services.AddBookingsDomain(configuration);
        // Stand-ins for the EF store and the BrightTide adapter, both scoped
        // in production; the point of this test is the DI shape, not their
        // behavior.
        services.AddScoped<IBookingStore, FakeBookingStore>();
        services.AddScoped<IBookingSystemProvider, InMemoryBookingSystemProvider>();
        // TriageSweep (scoped) needs an IHelpdeskProvider to construct;
        // ValidateOnBuild instantiates every registered service once, so
        // this stand-in must be present even though the test never sweeps.
        services.AddScoped<IHelpdeskProvider>(_ => new InMemoryHelpdeskProvider([]));
        services.AddTriageModule(configuration, bookingSystemConfigured: true);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var options = provider.GetRequiredService<IOptions<TriageOptions>>().Value;
        Assert.Equal(_packPath, options.RulePackPath);

        using var scope = provider.CreateScope();
        var processors = scope.ServiceProvider.GetServices<ITicketProcessor>();
        Assert.Contains(processors, p => p.Name == "booking-lookup");
    }
}
