using Microsoft.Extensions.Options;

namespace Xenaia.Domain.Bookings.Codes;

/// <summary>
/// Compiles both patterns at startup so a malformed tenant regex stops the
/// host instead of surfacing on the first parsed code (fail closed).
/// </summary>
public sealed class BookingsFormatValidator : IValidateOptions<BookingsFormatOptions>
{
    public ValidateOptionsResult Validate(string? name, BookingsFormatOptions options)
    {
        var failures = new List<string>();
        TryCompile(options.BookingCodePattern, "Tenant:Bookings:BookingCodePattern", failures);
        TryCompile(options.ProductCodePattern, "Tenant:Bookings:ProductCodePattern", failures);
        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void TryCompile(string pattern, string key, List<string> failures)
    {
        try
        {
            _ = CodeFormat.Create(pattern);
        }
        catch (InvalidCodeFormatException ex)
        {
            failures.Add($"{key}: {ex.Message}");
        }
    }
}
