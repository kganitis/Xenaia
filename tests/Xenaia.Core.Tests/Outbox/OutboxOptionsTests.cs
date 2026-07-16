using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Core.Outbox;

namespace Xenaia.Core.Tests.Outbox;

public class OutboxOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static OutboxOptions Bind(IConfiguration configuration)
        => new ServiceCollection()
            .AddValidatedOptions<OutboxOptions>(configuration)
            .BuildServiceProvider()
            .GetRequiredService<IOptions<OutboxOptions>>().Value;

    [Fact]
    public void Defaults_apply_when_section_present_but_sparse()
    {
        var options = Bind(Config(new() { ["Outbox:BatchSize"] = "25" }));

        Assert.Equal(10, options.PollIntervalSeconds);
        Assert.Equal(25, options.BatchSize);
    }

    [Fact]
    public void Missing_section_fails_closed_at_registration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddValidatedOptions<OutboxOptions>(Config([])));
    }

    [Fact]
    public void Out_of_range_poll_interval_fails_validation()
    {
        var configuration = Config(new() { ["Outbox:PollIntervalSeconds"] = "0" });

        Assert.Throws<OptionsValidationException>(() => Bind(configuration));
    }

    [Fact]
    public void Out_of_range_batch_size_fails_validation()
    {
        var configuration = Config(new() { ["Outbox:BatchSize"] = "0" });

        Assert.Throws<OptionsValidationException>(() => Bind(configuration));
    }
}
