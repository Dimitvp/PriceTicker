using PriceTicker.Core.Holdings;

namespace PriceTicker.Tests.Core;

public sealed class PositionTests
{
    [Fact]
    public void ValueAndGain_AreForTheWholePositionAgainstTheMoneyPutIn()
    {
        var position = new Position(quantity: 2.5m, invested: 250m);

        Assert.Equal(275m, position.ValueAt(110m));
        Assert.Equal(10m, position.GainPercent(110m));
        Assert.Equal(-20m, position.GainPercent(80m));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 0)]
    [InlineData(-1, 100)]
    public void Constructor_WithNonPositiveQuantityOrInvested_Throws(decimal quantity, decimal invested)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position(quantity, invested));
    }

    [Fact]
    public void Holding_WithoutPurchasePrice_HasNoPriceGain()
    {
        var holding = new Holding(null, PriceAlerts.None, new Position(1m, 100m));

        Assert.Null(holding.GainPercent(120m));
    }
}
