using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;

namespace Xenaia.Core.Tests.Options;

public class OptionsRegistrationTests
{
    private sealed class DemoOptions : ISectionOptions
    {
        public static string SectionName => "Demo";

        [Required(AllowEmptyStrings = false)]
        public string Name { get; init; } = "";
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Binds_and_validates_a_correct_section()
    {
        var config = Config(new() { ["Demo:Name"] = "Meridian Trails" });
        var provider = new ServiceCollection()
            .AddValidatedOptions<DemoOptions>(config)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DemoOptions>>().Value;

        Assert.Equal("Meridian Trails", options.Name);
    }

    [Fact]
    public void Missing_section_fails_at_registration_time()
    {
        var config = Config(new());
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddValidatedOptions<DemoOptions>(config));
    }

    [Fact]
    public void Invalid_values_fail_on_first_resolution()
    {
        var config = Config(new() { ["Demo:Name"] = "" });
        var provider = new ServiceCollection()
            .AddValidatedOptions<DemoOptions>(config)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DemoOptions>>().Value);
    }
}
