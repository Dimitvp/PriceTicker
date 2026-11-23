namespace PriceTicker.Core.Quotes;

/// <summary>Whether the exchange is trading, as seen when the quote was fetched.</summary>
public enum MarketState
{
    Unknown,

    /// <summary>The regular session is running.</summary>
    Open,

    /// <summary>The next regular session (today, or after a weekend or holiday) has not started yet; see <see cref="Market.OpensAt"/>.</summary>
    BeforeOpen,

    /// <summary>Outside the regular session (after the close, a weekend or a holiday).</summary>
    Closed,
}

/// <summary>Where a quote comes from and whether that market is trading.</summary>
public sealed record Market
{
    public Market(string exchange, MarketState state, DateTimeOffset? opensAt = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Exchange = exchange;
        State = state;
        OpensAt = state == MarketState.BeforeOpen ? opensAt : null;
    }

    /// <summary>Display name, e.g. "XETRA", "Milan", "Paris"; may be empty when unknown.</summary>
    public string Exchange { get; }

    public MarketState State { get; }

    /// <summary>Start of the next regular session, when it is still ahead.</summary>
    public DateTimeOffset? OpensAt { get; }

    /// <summary>The state at <paramref name="now"/> for a regular session from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static Market ForSession(string exchange, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        if (end <= start)
        {
            return new Market(exchange, MarketState.Unknown);
        }

        if (now < start)
        {
            return new Market(exchange, MarketState.BeforeOpen, start);
        }

        return new Market(exchange, now < end ? MarketState.Open : MarketState.Closed);
    }
}
