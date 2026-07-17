using Xunit;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Tests.Rules;

public class TextNormalizerTests
{
    [Fact]
    public void Strips_tags_and_decodes_entities()
    {
        var text = TextNormalizer.Normalize("<p>Guided tour &amp; tasting</p>");

        Assert.Equal("Guided tour & tasting", text);
    }

    [Fact]
    public void Table_row_cells_join_on_one_line()
    {
        var html = "<table><tr><th>Date</th><td>25/12/2026</td><td>Time</td><td>14:00</td></tr></table>";

        var text = TextNormalizer.Normalize(html);

        Assert.Contains("Date 25/12/2026 Time 14:00", text);
    }

    [Fact]
    public void Block_elements_break_lines()
    {
        var html = "<h2>Kayak Rental (MTP-KAYA)</h2><p>Half day</p><div>Meridian Trails</div>";

        var text = TextNormalizer.Normalize(html);

        Assert.Equal("Kayak Rental (MTP-KAYA)\nHalf day\nMeridian Trails", text);
    }

    [Fact]
    public void Whitespace_runs_collapse_and_blank_lines_drop()
    {
        var html = "<p>New   booking\t received</p><p>  </p><p>Thanks</p>";

        var text = TextNormalizer.Normalize(html);

        Assert.Equal("New booking received\nThanks", text);
    }

    [Fact]
    public void Plain_text_passes_through_normalized()
    {
        Assert.Equal("just plain text", TextNormalizer.Normalize("just   plain text"));
    }

    [Fact]
    public void Script_and_style_content_is_removed()
    {
        var html = "<style>h2 { color: red }</style><p>visible</p><script>alert(1)</script>";

        Assert.Equal("visible", TextNormalizer.Normalize(html));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_yields_empty(string input)
    {
        Assert.Equal("", TextNormalizer.Normalize(input));
    }
}
