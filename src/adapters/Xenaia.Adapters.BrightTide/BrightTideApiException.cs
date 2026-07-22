using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Adapters.BrightTide;

/// <summary>
/// A non-success HTTP outcome from BrightTide that falls outside the adapter's
/// documented conventions (such as 404 -> null on a read). Carries the vendor
/// status code and a truncated copy of the response body for diagnostics.
/// A <see cref="BookingSystemException"/>, so callers treat it as a retryable
/// sync failure like any other transport fault.
/// </summary>
public sealed class BrightTideApiException : BookingSystemException
{
    private const int MaxBodyLength = 500;

    public int StatusCode { get; }

    public string ResponseBody { get; }

    public BrightTideApiException(int statusCode, string responseBody)
        : base($"BrightTide API returned HTTP {statusCode}: {Truncate(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = Truncate(responseBody);
    }

    private static string Truncate(string? body)
    {
        body ??= "";
        return body.Length <= MaxBodyLength ? body : body[..MaxBodyLength] + "...";
    }
}
