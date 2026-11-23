namespace PriceTicker.Core.Bullion;

/// <summary>
/// One dealer's prices for a physical bar, from the customer's side: <see cref="BuyPrice"/> is what
/// you pay the dealer ("Продаваме" on the dealer's site), <see cref="BuybackPrice"/> is what the
/// dealer pays you ("Купуваме").
/// </summary>
public sealed record BullionQuote
{
    public BullionQuote(string dealer, decimal buyPrice, decimal buybackPrice, DateTimeOffset asOf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dealer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buyPrice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buybackPrice);

        Dealer = dealer;
        BuyPrice = buyPrice;
        BuybackPrice = buybackPrice;
        AsOf = asOf;
    }

    public string Dealer { get; }

    public decimal BuyPrice { get; }

    public decimal BuybackPrice { get; }

    /// <summary>When the prices were read.</summary>
    public DateTimeOffset AsOf { get; }
}

/// <summary>Either a dealer's <see cref="BullionQuote"/> or why it could not be read.</summary>
public abstract record BullionQuoteResult
{
    private protected BullionQuoteResult(string dealer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dealer);
        Dealer = dealer;
    }

    public string Dealer { get; }
}

public sealed record BullionQuoteAvailable : BullionQuoteResult
{
    public BullionQuoteAvailable(BullionQuote quote)
        : base(quote?.Dealer ?? throw new ArgumentNullException(nameof(quote)))
    {
        Quote = quote;
    }

    public BullionQuote Quote { get; }
}

public sealed record BullionQuoteUnavailable : BullionQuoteResult
{
    public BullionQuoteUnavailable(string dealer, string reason)
        : base(dealer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Short, user-facing explanation.</summary>
    public string Reason { get; }
}
