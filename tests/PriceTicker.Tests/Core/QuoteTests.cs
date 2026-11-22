using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Core;

public sealed class QuoteTests
{
    [Fact]
    public void ChangePercent_WithPreviousClose_IsRelativeChange()
    {
        var quote = new Quote(TestInstruments.Create("a"), price: 110m, previousClose: 100m, "EUR", DateTimeOffset.UnixEpoch);

        Assert.Equal(10m, quote.ChangePercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ChangePercent_WithoutUsablePreviousClose_IsNull(int? previousClose)
    {
        var quote = new Quote(TestInstruments.Create("a"), 110m, previousClose, "EUR", DateTimeOffset.UnixEpoch);

        Assert.Null(quote.ChangePercent);
    }

    [Fact]
    public void Constructor_WithNegativePrice_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Quote(TestInstruments.Create("a"), -1m, null, "EUR", DateTimeOffset.UnixEpoch));
    }
}
