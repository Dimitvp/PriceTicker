using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>
/// Prices from a venue that trades longer than the instruments' home exchanges (Tradegate,
/// 08:00–22:00 CET), so the move before the open and after the close is visible.
/// </summary>
public interface IExtendedHoursQuoteService
{
    /// <summary>
    /// One result per instrument, in order. Instruments the venue does not trade, or that have no
    /// ISIN, come back as <see cref="QuoteUnavailable"/>. Never throws for a failed request.
    /// </summary>
    Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(IReadOnlyList<Instrument> instruments, CancellationToken cancellationToken);
}
