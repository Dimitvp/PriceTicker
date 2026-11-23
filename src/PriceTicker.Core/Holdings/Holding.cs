namespace PriceTicker.Core.Holdings;

/// <summary>How urgently a holding's price asks for attention under the exit rules.</summary>
public enum AlertLevel
{
    None,

    /// <summary>The review alert (−20%) has been reached: check whether the thesis still holds.</summary>
    Review,

    /// <summary>A sell alert has been reached: the exit (−40%) or the take-profit (+50%) level.</summary>
    Sell,
}

/// <summary>
/// Price alert levels, as set in the broker. Any of them may be absent; a price exactly at a
/// level counts as having reached it, matching the broker's "less/greater than or equal" alerts.
/// </summary>
public sealed record PriceAlerts(decimal? ReviewAtOrBelow, decimal? SellAtOrBelow, decimal? SellAtOrAbove)
{
    public static PriceAlerts None { get; } = new(null, null, null);

    /// <summary>The most urgent level the price has reached; selling outranks reviewing.</summary>
    public AlertLevel Evaluate(decimal price)
    {
        if (price <= SellAtOrBelow || price >= SellAtOrAbove)
        {
            return AlertLevel.Sell;
        }

        return price <= ReviewAtOrBelow ? AlertLevel.Review : AlertLevel.None;
    }
}

/// <summary>What was paid for an instrument and the alerts that watch it.</summary>
public sealed record Holding
{
    public Holding(decimal purchasePrice, PriceAlerts alerts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(purchasePrice);
        ArgumentNullException.ThrowIfNull(alerts);

        PurchasePrice = purchasePrice;
        Alerts = alerts;
    }

    /// <summary>Average purchase price, excluding commissions (the base the alerts were set from).</summary>
    public decimal PurchasePrice { get; }

    public PriceAlerts Alerts { get; }

    public decimal GainPercent(decimal price) => (price - PurchasePrice) / PurchasePrice * 100m;
}
