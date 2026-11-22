using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>
/// Outcome of asking for one instrument's price. A closed hierarchy: either
/// <see cref="QuoteAvailable"/> or <see cref="QuoteUnavailable"/>. A missing price is an
/// expected outcome for a ticker, so it is modelled as data rather than as an exception.
/// </summary>
public abstract record QuoteResult : InstrumentResult
{
    private protected QuoteResult(Instrument instrument)
        : base(instrument)
    {
    }
}

public sealed record QuoteAvailable : QuoteResult
{
    public QuoteAvailable(Quote quote)
        : base(quote?.Instrument ?? throw new ArgumentNullException(nameof(quote)))
    {
        Quote = quote;
    }

    public Quote Quote { get; }
}

public sealed record QuoteUnavailable : QuoteResult
{
    public QuoteUnavailable(Instrument instrument, string reason)
        : base(instrument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Short, user-facing explanation. Never contains secrets.</summary>
    public string Reason { get; }

    /// <summary>One <see cref="QuoteUnavailable"/> per instrument, all with the same reason.</summary>
    public static QuoteResult[] ForAll(IEnumerable<Instrument> instruments, string reason)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        return [.. instruments.Select(instrument => new QuoteUnavailable(instrument, reason))];
    }
}
