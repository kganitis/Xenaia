using Microsoft.Extensions.Options;

namespace Xenaia.Domain.Bookings.Codes;

/// <summary>
/// The tenant's compiled code formats, for boundary code that parses
/// external values into BookingCode / ProductCode.
/// </summary>
public sealed class CodeFormats(IOptions<BookingsFormatOptions> options)
{
    public CodeFormat BookingCode { get; } = CodeFormat.Create(options.Value.BookingCodePattern);

    public CodeFormat ProductCode { get; } = CodeFormat.Create(options.Value.ProductCodePattern);
}
