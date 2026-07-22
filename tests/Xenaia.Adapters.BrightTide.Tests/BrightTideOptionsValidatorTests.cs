using Xenaia.Adapters.BrightTide;
using Xunit;

namespace Xenaia.Adapters.BrightTide.Tests;

public class BrightTideOptionsValidatorTests
{
    private static readonly BrightTideOptionsValidator Validator = new();

    [Fact]
    public void Valid_https_configuration_passes()
    {
        var result = Validator.Validate(null, new BrightTideOptions
        {
            BaseUrl = "https://api.brighttide.example/v1/",
            ApiKey = "key",
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Localhost_may_use_plain_http()
    {
        var result = Validator.Validate(null, new BrightTideOptions
        {
            BaseUrl = "http://localhost:5000/",
            ApiKey = "key",
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Non_localhost_http_fails_closed()
    {
        var result = Validator.Validate(null, new BrightTideOptions
        {
            BaseUrl = "http://api.brighttide.example/",
            ApiKey = "key",
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Relative_base_url_fails_closed()
    {
        var result = Validator.Validate(null, new BrightTideOptions
        {
            BaseUrl = "/v1",
            ApiKey = "key",
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Missing_api_key_fails_closed()
    {
        var result = Validator.Validate(null, new BrightTideOptions
        {
            BaseUrl = "https://api.brighttide.example/v1/",
            ApiKey = "",
        });

        Assert.True(result.Failed);
    }
}
