namespace Xenaia.Api;

/// <summary>
/// Gates every <c>/api/*</c> request behind the static <c>Api:ApiKey</c>
/// (spec section 10). Fail closed: when the key is unset the surface returns
/// 503 (a tenant never accidentally exposes an open write surface); a request
/// with a missing or wrong <c>X-Api-Key</c> header returns 401. Non-<c>/api</c>
/// paths (notably <c>/health</c>) pass straight through untouched.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string HeaderName = "X-Api-Key";

    private readonly string? _apiKey = configuration["Api:ApiKey"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var provided = context.Request.Headers[HeaderName].ToString();
        if (!string.Equals(provided, _apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
