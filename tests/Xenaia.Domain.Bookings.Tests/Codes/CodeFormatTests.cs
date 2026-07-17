using Xenaia.Domain.Bookings.Codes;

namespace Xenaia.Domain.Bookings.Tests.Codes;

public class CodeFormatTests
{
    [Fact]
    public void Valid_pattern_compiles_and_matches()
    {
        var format = CodeFormat.Create("^MT-[A-Z0-9]{8}$");

        Assert.True(format.Matches("MT-7KQ2XY9Z"));
        Assert.False(format.Matches("XX-7KQ2XY9Z"));
        Assert.Equal("^MT-[A-Z0-9]{8}$", format.Pattern);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_pattern_is_rejected(string pattern)
    {
        Assert.Throws<InvalidCodeFormatException>(() => CodeFormat.Create(pattern));
    }

    [Fact]
    public void Malformed_pattern_is_rejected()
    {
        Assert.Throws<InvalidCodeFormatException>(() => CodeFormat.Create("(["));
    }

    [Fact]
    public void Catastrophic_backtracking_times_out_and_fails_closed()
    {
        // Tenant regex is untrusted input; a pathological pattern must not
        // hang the host. On timeout, the value simply does not match.
        var format = CodeFormat.Create("^(a+)+$");
        var hostile = new string('a', 5000) + "b";

        Assert.False(format.Matches(hostile));
    }
}
