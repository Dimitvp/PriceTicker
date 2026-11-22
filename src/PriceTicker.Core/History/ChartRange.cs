namespace PriceTicker.Core.History;

/// <summary>How far back a price chart reaches.</summary>
public enum ChartRange
{
    /// <summary>The last 24 hours, or the last trading session for exchange-traded instruments.</summary>
    Day,
    Week,
    ThreeMonths,
    SixMonths,
    Year,
}
