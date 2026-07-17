using Xunit;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Tests.Rules;

public class TimedRegexTests
{
    [Fact]
    public void Matches_case_insensitively_and_exposes_named_captures()
    {
        var regex = TimedRegex.Create(@"^new booking\s+\[(?<bookingCode>MT-[A-Z0-9]{8})\]");

        var match = regex.Match("New Booking [MT-7Q2K9F4A]", out var timedOut);

        Assert.False(timedOut);
        Assert.NotNull(match);
        Assert.Equal("MT-7Q2K9F4A", match!.Groups["bookingCode"].Value);
        Assert.Equal(["bookingCode"], regex.CaptureNames);
    }

    [Fact]
    public void Non_matching_input_returns_null()
    {
        var regex = TimedRegex.Create("^Payment received");

        Assert.Null(regex.Match("Refund requested", out var timedOut));
        Assert.False(timedOut);
    }

    [Fact]
    public void Invalid_pattern_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() => TimedRegex.Create("(unclosed"));
    }

    [Fact]
    public void Catastrophic_backtracking_times_out_as_no_match()
    {
        var regex = TimedRegex.Create(@"^(a+)+$");
        var hostile = new string('a', 60) + "!";

        var match = regex.Match(hostile, out var timedOut);

        Assert.Null(match);
        Assert.True(timedOut);
    }
}
