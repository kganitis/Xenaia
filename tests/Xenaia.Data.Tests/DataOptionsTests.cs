using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;

namespace Xenaia.Data.Tests;

public class DataOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static DataOptions Bind(IConfiguration configuration)
        => new ServiceCollection()
            .AddValidatedOptions<DataOptions>(configuration)
            .BuildServiceProvider()
            .GetRequiredService<IOptions<DataOptions>>().Value;

    [Fact]
    public void Binds_connection_string_and_defaults_auto_migrate_on()
    {
        var options = Bind(Config(new()
        {
            ["Data:ConnectionString"] = "Host=localhost;Database=xenaia",
        }));

        Assert.Equal("Host=localhost;Database=xenaia", options.ConnectionString);
        Assert.True(options.AutoMigrate);
    }

    [Fact]
    public void Missing_section_fails_closed_at_registration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddValidatedOptions<DataOptions>(Config([])));
    }

    [Fact]
    public void Empty_connection_string_fails_validation()
    {
        var configuration = Config(new()
        {
            ["Data:ConnectionString"] = "",
            ["Data:AutoMigrate"] = "false",
        });

        Assert.Throws<OptionsValidationException>(() => Bind(configuration));
    }
}
