using PriceTicker.Core.Instruments;

namespace PriceTicker.Tests.Core;

public sealed class IsinTests
{
    [Theory]
    [InlineData("US0378331005")]
    [InlineData("IE00B4L5Y983")]
    [InlineData("DE0005140008")]
    [InlineData(" us0378331005 ")]
    public void TryParse_WithValidIsin_Succeeds(string text)
    {
        Assert.True(Isin.TryParse(text, out var isin));
        Assert.Equal(text.Trim().ToUpperInvariant(), isin.Value);
    }

    [Theory]
    [InlineData("US0378331006")]   // wrong check digit
    [InlineData("US0378331O05")]   // letter O for zero
    [InlineData("US037833100")]    // too short
    [InlineData("1S0378331005")]   // country code with a digit
    [InlineData("US03783310-5")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_WithInvalidIsin_Fails(string? text)
    {
        Assert.False(Isin.TryParse(text, out var isin));
        Assert.Null(isin);
    }
}
