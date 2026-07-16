using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Core;
using Xenaia.Core.BusinessHours;
using Xenaia.Core.Events;
using Xenaia.Core.Notifications;
using Xenaia.Core.Tenancy;

namespace Xenaia.Core.Tests;

public class CoreServiceCollectionExtensionsTests
{
    private static IConfiguration ValidTenantConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:BusinessName"] = "Meridian Trails",
            ["Tenant:TimeZone"] = "America/New_York",
            ["Tenant:Locales:0"] = "en-US",
        })
        .Build();

    [Fact]
    public void Registers_core_services_resolvable_from_valid_config()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddXenaiaCore(ValidTenantConfig())
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDomainEventDispatcher>());
        Assert.NotNull(provider.GetRequiredService<INotificationService>());
        Assert.NotNull(provider.GetRequiredService<IBusinessHoursService>());
        Assert.Equal("Meridian Trails",
            provider.GetRequiredService<IOptions<TenantProfileOptions>>().Value.BusinessName);
    }

    [Fact]
    public void Missing_tenant_section_fails_closed()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();

        Assert.Throws<InvalidOperationException>(
            () => services.AddXenaiaCore(emptyConfig));
    }

    [Fact]
    public void Semantically_invalid_tenant_profile_fails_closed()
    {
        var badConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenant:BusinessName"] = "Meridian Trails",
                ["Tenant:TimeZone"] = "Nowhere/Imaginary",
                ["Tenant:Locales:0"] = "en-US",
            })
            .Build();
        var provider = new ServiceCollection()
            .AddLogging()
            .AddXenaiaCore(badConfig)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TenantProfileOptions>>().Value);
    }

    [Fact]
    public void Business_hours_bind_from_config_strings_into_working_service()
    {
        // Meridian Trails, America/New_York (UTC-5 in January): Monday 09:00-17:00
        // local is 14:00-22:00 UTC. 2026-01-12 (also a Monday) is a configured holiday,
        // so it must read closed even though it falls inside the weekly window.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenant:BusinessName"] = "Meridian Trails",
                ["Tenant:TimeZone"] = "America/New_York",
                ["Tenant:Locales:0"] = "en-US",
                ["Tenant:BusinessHours:Weekly:0:Day"] = "Monday",
                ["Tenant:BusinessHours:Weekly:0:Open"] = "09:00",
                ["Tenant:BusinessHours:Weekly:0:Close"] = "17:00",
                ["Tenant:BusinessHours:Holidays:0"] = "2026-01-12",
            })
            .Build();

        var provider = new ServiceCollection()
            .AddLogging()
            .AddXenaiaCore(config)
            .BuildServiceProvider();
        var businessHours = provider.GetRequiredService<IBusinessHoursService>();

        // Monday 2026-01-05, 15:00 UTC == 10:00 New York: inside the bound window.
        var mondayInsideWindowUtc = new DateTimeOffset(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);
        Assert.True(businessHours.IsOpenAt(mondayInsideWindowUtc));

        // Monday 2026-01-12, 15:00 UTC == 10:00 New York: inside the weekly window,
        // but this date is the configured holiday, so it must read closed.
        var holidayUtc = new DateTimeOffset(2026, 1, 12, 15, 0, 0, TimeSpan.Zero);
        Assert.False(businessHours.IsOpenAt(holidayUtc));
    }
}
