namespace PriceTicker.Core.Holdings;

/// <summary>
/// How much of an instrument is held and how much money went into it in total (commissions
/// included, as the broker's cost basis). Suits positions built up by regular purchases, where no
/// single purchase price is meaningful.
/// </summary>
public sealed record Position
{
    public Position(decimal quantity, decimal invested, DateOnly? firstPurchase = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(invested);

        Quantity = quantity;
        Invested = invested;
        FirstPurchase = firstPurchase;
    }

    public decimal Quantity { get; }

    public decimal Invested { get; }

    /// <summary>Date of the first purchase, when known: how long the position has been held.</summary>
    public DateOnly? FirstPurchase { get; }

    /// <summary>Time held up to <paramref name="today"/>; null without <see cref="FirstPurchase"/>.</summary>
    public HoldingPeriod? HeldFor(DateOnly today) =>
        FirstPurchase is { } first ? HoldingPeriod.Between(first, today) : null;

    /// <summary>What selling everything at <paramref name="price"/> would bring, before the sale's own costs.</summary>
    public decimal ValueAt(decimal price) => Quantity * price;

    /// <summary>Gain or loss of the whole position against what was put in, in percent.</summary>
    public decimal GainPercent(decimal price) => (ValueAt(price) - Invested) / Invested * 100m;
}
