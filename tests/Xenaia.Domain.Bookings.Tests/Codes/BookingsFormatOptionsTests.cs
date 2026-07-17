using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Codes;

namespace Xenaia.Domain.Bookings.Tests.Codes;

public class BookingsFormatOptionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddBookingsDomainForTest(settings)
            .BuildServiceProvider();

    [Fact]
    public void Valid_patterns_produce_usable_code_formats()
    {
        using var provider = Build(new()
        {
            ["Tenant:Bookings:BookingCodePattern"] = "^MT-[A-Z0-9]{8}$",
            ["Tenant:Bookings:ProductCodePattern"] = "^MTP-[A-Z0-9]{4}$",
        });

        var formats = provider.GetRequiredService<CodeFormats>();

        Assert.True(formats.BookingCode.Matches("MT-7KQ2XY9Z"));
        Assert.True(formats.ProductCode.Matches("MTP-K4Y2"));
    }

    [Fact]
    public void Malformed_pattern_fails_options_validation()
    {
        using var provider = Build(new()
        {
            ["Tenant:Bookings:BookingCodePattern"] = "([",
            ["Tenant:Bookings:ProductCodePattern"] = "^MTP-[A-Z0-9]{4}$",
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BookingsFormatOptions>>().Value);

        Assert.Contains("BookingCodePattern", ex.Message);
    }

    [Fact]
    public void Missing_section_throws_immediately()
    {
        // Absent tenant configuration is never silently defaulted.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Assert.ThrowsAny<InvalidOperationException>(
            () => services.AddBookingsDomain(configuration));
    }

    [Fact]
    public void Validator_reports_both_bad_patterns()
    {
        var result = new BookingsFormatValidator().Validate(null, new BookingsFormatOptions
        {
            BookingCodePattern = "([",
            ProductCodePattern = "*invalid*",
        });

        Assert.True(result.Failed);
        Assert.Contains("BookingCodePattern", result.FailureMessage);
        Assert.Contains("ProductCodePattern", result.FailureMessage);
    }
}

internal static class TestRegistration
{
    public static IServiceCollection AddBookingsDomainForTest(
        this IServiceCollection services, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return services.AddBookingsDomain(configuration);
    }
}
