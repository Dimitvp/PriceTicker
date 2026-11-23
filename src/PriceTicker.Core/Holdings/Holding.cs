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

/// <summary>
/// What is known about an instrument that is held: a one-off purchase price that the broker
/// alerts were set from, the size of the position, or both.
/// </summary>
public sealed record Holding
{
    public Holding(decimal? purchasePrice, PriceAlerts alerts, Position? position = null)
    {
        if (purchasePrice is { } price)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        }

        ArgumentNullException.ThrowIfNull(alerts);

        PurchasePrice = purchasePrice;
        Alerts = alerts;
        Position = position;
    }

    /// <summary>
    /// Price of a one-off purchase, excluding commissions (the base the alerts were set from).
    /// None for a position built up by regular purchases.
    /// </summary>
    public decimal? PurchasePrice { get; }

    public PriceAlerts Alerts { get; }

    public Position? Position { get; }

    /// <summary>Change against <see cref="PurchasePrice"/> in percent; null without one.</summary>
    public decimal? GainPercent(decimal price) => (price - PurchasePrice) / PurchasePrice * 100m;
}
