using PriceTicker.Core.Holdings;

namespace PriceTicker.Tests.Core;

public sealed class HoldingTests
{
    // Bought at 100: review at −20%, exit at −40%, sell half at +50%.
    private static readonly PriceAlerts Alerts = new(ReviewAtOrBelow: 80m, SellAtOrBelow: 60m, SellAtOrAbove: 150m);

    [Theory]
    [InlineData(100, AlertLevel.None)]
    [InlineData(80.01, AlertLevel.None)]
    [InlineData(80, AlertLevel.Review)]
    [InlineData(70, AlertLevel.Review)]
    [InlineData(60, AlertLevel.Sell)]
    [InlineData(50, AlertLevel.Sell)]
    [InlineData(149.99, AlertLevel.None)]
    [InlineData(150, AlertLevel.Sell)]
    public void Evaluate_MatchesTheBrokerAlerts(decimal price, AlertLevel expected)
    {
        Assert.Equal(expected, Alerts.Evaluate(price));
    }

    [Fact]
    public void Evaluate_WithoutAlerts_IsNone()
    {
        Assert.Equal(AlertLevel.None, PriceAlerts.None.Evaluate(1m));
    }

    [Fact]
    public void GainPercent_IsRelativeToPurchasePrice()
    {
        var holding = new Holding(100m, PriceAlerts.None);

        Assert.Equal(-30m, holding.GainPercent(70m));
    }

    [Fact]
    public void Constructor_WithNonPositivePurchasePrice_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Holding(0m, PriceAlerts.None));
    }
}
