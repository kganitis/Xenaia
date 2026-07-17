namespace Xenaia.Domain.Bookings.Codes;

/// <summary>
/// A product's catalog code. The type hard-codes no grammar; the format is
/// tenant configuration supplied at the boundary that parses the code.
/// </summary>
public sealed record ProductCode
{
    public string Value { get; }

    private ProductCode(string value) => Value = value;

    public static ProductCode Create(string value, CodeFormat format)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidCodeException("Product code is empty.");
        if (!format.Matches(value))
            throw new InvalidCodeException(
                $"Product code '{value}' does not match the tenant format '{format.Pattern}'.");
        return new ProductCode(value);
    }

    /// <summary>
    /// Persistence rehydration only: the stored value was validated when
    /// written, and the tenant format may have changed since.
    /// </summary>
    public static ProductCode FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
