using Xenaia.Domain.Bookings.Codes;

namespace Xenaia.Domain.Bookings.Tests.Codes;

public class CodeValueObjectTests
{
    private static readonly CodeFormat BookingFormat = CodeFormat.Create("^MT-[A-Z0-9]{8}$");
    private static readonly CodeFormat ProductFormat = CodeFormat.Create("^MTP-[A-Z0-9]{4}$");

    [Fact]
    public void Matching_value_creates_the_code()
    {
        var code = BookingCode.Create("MT-7KQ2XY9Z", BookingFormat);

        Assert.Equal("MT-7KQ2XY9Z", code.Value);
        Assert.Equal("MT-7KQ2XY9Z", code.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mt-7kq2xy9z")]
    [InlineData("MT-SHORT")]
    public void Non_matching_value_is_rejected(string value)
    {
        Assert.Throws<InvalidCodeException>(() => BookingCode.Create(value, BookingFormat));
    }

    [Fact]
    public void Codes_are_equal_by_value()
    {
        Assert.Equal(
            BookingCode.Create("MT-7KQ2XY9Z", BookingFormat),
            BookingCode.Create("MT-7KQ2XY9Z", BookingFormat));
    }

    [Fact]
    public void FromTrusted_skips_format_validation()
    {
        // Rehydration path: the database value was validated when written,
        // and the tenant may have changed the format since.
        var code = BookingCode.FromTrusted("LEGACY-1");

        Assert.Equal("LEGACY-1", code.Value);
    }

    [Fact]
    public void Product_codes_behave_the_same()
    {
        var code = ProductCode.Create("MTP-K4Y2", ProductFormat);

        Assert.Equal("MTP-K4Y2", code.Value);
        Assert.Throws<InvalidCodeException>(() => ProductCode.Create("nope", ProductFormat));
        Assert.Equal(code, ProductCode.FromTrusted("MTP-K4Y2"));
    }
}
