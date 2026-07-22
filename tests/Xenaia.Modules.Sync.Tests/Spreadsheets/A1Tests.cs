using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests.Spreadsheets;

public class A1Tests
{
    [Fact]
    public void Plain_name_is_left_unquoted() =>
        Assert.Equal("Sheet1", A1.QuoteTab("Sheet1"));

    [Fact]
    public void Name_with_a_space_is_wrapped_in_single_quotes() =>
        Assert.Equal("'My Sheet'", A1.QuoteTab("My Sheet"));

    [Fact]
    public void Embedded_single_quote_is_doubled_inside_the_quotes() =>
        Assert.Equal("'O''Brien Tours'", A1.QuoteTab("O'Brien Tours"));
}
