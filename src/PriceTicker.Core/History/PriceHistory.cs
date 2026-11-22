using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.History;

public sealed record PricePoint(DateTimeOffset Time, decimal Price);

/// <summary>An instrument's prices over a <see cref="ChartRange"/>, oldest first.</summary>
public sealed record PriceHistory
{
    public PriceHistory(Instrument instrument, ChartRange range, IEnumerable<PricePoint> points, decimal? previousClose)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(points);

        Points = [.. points.OrderBy(point => point.Time)];
        if (Points.Count == 0)
        {
            throw new ArgumentException("A price history needs at least one point.", nameof(points));
        }

        Instrument = instrument;
        Range = range;
        Baseline = previousClose is > 0m ? previousClose.Value : Points[0].Price;
    }

    public Instrument Instrument { get; }

    public ChartRange Range { get; }

    public IReadOnlyList<PricePoint> Points { get; }

    /// <summary>
    /// The price the range's change is measured from: the close before the range began when the
    /// provider reports it, otherwise the first point.
    /// </summary>
    public decimal Baseline { get; }

    /// <summary>Change from <see cref="Baseline"/> to <paramref name="price"/> in percent, or null when undefined.</summary>
    public decimal? ChangePercentTo(decimal price) =>
        Baseline > 0m ? (price - Baseline) / Baseline * 100m : null;
}
