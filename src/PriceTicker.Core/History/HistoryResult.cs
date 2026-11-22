using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.History;

/// <summary>
/// Outcome of asking for one instrument's price history: either <see cref="HistoryAvailable"/>
/// or <see cref="HistoryUnavailable"/>. A missing chart is expected, so it is data, not an exception.
/// </summary>
public abstract record HistoryResult : InstrumentResult
{
    private protected HistoryResult(Instrument instrument)
        : base(instrument)
    {
    }
}

public sealed record HistoryAvailable : HistoryResult
{
    public HistoryAvailable(PriceHistory history)
        : base(history?.Instrument ?? throw new ArgumentNullException(nameof(history)))
    {
        History = history;
    }

    public PriceHistory History { get; }
}

public sealed record HistoryUnavailable : HistoryResult
{
    public HistoryUnavailable(Instrument instrument, string reason)
        : base(instrument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Short, user-facing explanation. Never contains secrets.</summary>
    public string Reason { get; }

    /// <summary>One <see cref="HistoryUnavailable"/> per instrument, all with the same reason.</summary>
    public static HistoryResult[] ForAll(IEnumerable<Instrument> instruments, string reason)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        return [.. instruments.Select(instrument => new HistoryUnavailable(instrument, reason))];
    }
}
