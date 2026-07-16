using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xenaia.Core.Outbox;

namespace Xenaia.Data.Tests;

public class DataRegistrationTests
{
    private static IConfiguration ValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Data:ConnectionString"] = "Host=localhost;Database=xenaia",
            ["Outbox:BatchSize"] = "50",
        })
        .Build();

    [Fact]
    public void Registers_drainer_as_hosted_service()
    {
        var services = new ServiceCollection().AddXenaiaData(ValidConfig());

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(OutboxDrainerService));
    }

    [Fact]
    public void Missing_data_section_fails_closed()
    {
        var services = new ServiceCollection();
        var empty = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => services.AddXenaiaData(empty));
    }
}
