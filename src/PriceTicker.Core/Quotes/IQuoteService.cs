using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>Fetches prices for a mixed list of instruments, whatever provider each one uses.</summary>
public interface IQuoteService
{
    /// <summary>
    /// Returns one result per instrument, in the order given. Never throws for data
    /// problems; throws only on cancellation.
    /// </summary>
    Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken);
}
