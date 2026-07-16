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
}
