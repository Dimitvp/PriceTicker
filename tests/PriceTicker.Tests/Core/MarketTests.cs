using PriceTicker.Core.Quotes;

namespace PriceTicker.Tests.Core;

public sealed class MarketTests
{
    // A Xetra regular session: 09:00–17:30 CET on 23.11.2026.
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 23, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ForSession_BeforeStart_IsBeforeOpen_WithOpeningTime()
    {
        var market = Market.ForSession("XETRA", Start, End, Start.AddMinutes(-90));

        Assert.Equal(MarketState.BeforeOpen, market.State);
        Assert.Equal(Start, market.OpensAt);
    }

    [Theory]
    [InlineData(0, MarketState.Open)]
    [InlineData(300, MarketState.Open)]
    [InlineData(510, MarketState.Closed)]
    [InlineData(900, MarketState.Closed)]
    public void ForSession_DuringAndAfter(int minutesAfterStart, MarketState expected)
    {
        var market = Market.ForSession("XETRA", Start, End, Start.AddMinutes(minutesAfterStart));

        Assert.Equal(expected, market.State);
        Assert.Null(market.OpensAt);
    }

    [Fact]
    public void ForSession_WithBrokenSession_IsUnknown()
    {
        Assert.Equal(MarketState.Unknown, Market.ForSession("XETRA", End, Start, Start).State);
    }
}
