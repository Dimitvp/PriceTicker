using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>A price observed for an instrument at a point in time.</summary>
public sealed record Quote
{
    public Quote(Instrument instrument, decimal price, decimal? previousClose, string currency, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentNullException.ThrowIfNull(currency);

        Instrument = instrument;
        Price = price;
        PreviousClose = previousClose;
        Currency = currency;
        AsOf = asOf;
    }

    public Instrument Instrument { get; }

    public decimal Price { get; }

    /// <summary>Close of the previous session, when the provider reports one.</summary>
    public decimal? PreviousClose { get; }

    /// <summary>ISO currency code, or empty when the provider does not report it.</summary>
    public string Currency { get; }

    public DateTimeOffset AsOf { get; }

    /// <summary>Change against the previous close in percent, or <c>null</c> when it cannot be computed.</summary>
    public decimal? ChangePercent =>
        PreviousClose is { } previous && previous > 0m
            ? (Price - previous) / previous * 100m
            : null;
}
