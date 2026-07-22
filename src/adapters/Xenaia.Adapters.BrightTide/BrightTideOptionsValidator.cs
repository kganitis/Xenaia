using Microsoft.Extensions.Options;

namespace Xenaia.Adapters.BrightTide;

/// <summary>
/// Fail-closed startup gate for BrightTide options (model on
/// FreshdeskOptions' conventions): BaseUrl must be an absolute URL that uses
/// HTTPS unless it targets localhost, and ApiKey must be present. Invalid
/// configuration fails the host at ValidateOnStart rather than at first call.
/// </summary>
public sealed class BrightTideOptionsValidator : IValidateOptions<BrightTideOptions>
{
    public ValidateOptionsResult Validate(string? name, BrightTideOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            errors.Add("BrightTide:BaseUrl is required.");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("BrightTide:BaseUrl must be an absolute http or https URL.");
        }
        else if (uri.Scheme != Uri.UriSchemeHttps && !IsLocalhost(uri))
        {
            errors.Add("BrightTide:BaseUrl must use HTTPS unless it targets localhost.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            errors.Add("BrightTide:ApiKey is required.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsLocalhost(Uri uri) =>
        uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}
