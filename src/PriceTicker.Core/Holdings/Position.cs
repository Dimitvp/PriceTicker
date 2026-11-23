namespace PriceTicker.Core.Holdings;

/// <summary>
/// How much of an instrument is held and how much money went into it in total (commissions
/// included, as the broker's cost basis). Suits positions built up by regular purchases, where no
/// single purchase price is meaningful.
/// </summary>
public sealed record Position
{
    public Position(decimal quantity, decimal invested)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(invested);

        Quantity = quantity;
        Invested = invested;
    }

    public decimal Quantity { get; }

    public decimal Invested { get; }

    /// <summary>What selling everything at <paramref name="price"/> would bring, before the sale's own costs.</summary>
    public decimal ValueAt(decimal price) => Quantity * price;

    /// <summary>Gain or loss of the whole position against what was put in, in percent.</summary>
    public decimal GainPercent(decimal price) => (ValueAt(price) - Invested) / Invested * 100m;
}
