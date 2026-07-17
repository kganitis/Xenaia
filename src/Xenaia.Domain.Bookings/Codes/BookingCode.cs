namespace Xenaia.Domain.Bookings.Codes;

/// <summary>
/// A booking's external code. The type hard-codes no grammar; the format is
/// tenant configuration supplied at the boundary that parses the code.
/// </summary>
public sealed record BookingCode
{
    public string Value { get; }

    private BookingCode(string value) => Value = value;

    public static BookingCode Create(string value, CodeFormat format)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidCodeException("Booking code is empty.");
        if (!format.Matches(value))
            throw new InvalidCodeException(
                $"Booking code '{value}' does not match the tenant format '{format.Pattern}'.");
        return new BookingCode(value);
    }

    /// <summary>
    /// Persistence rehydration only: the stored value was validated when
    /// written, and the tenant format may have changed since.
    /// </summary>
    public static BookingCode FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
