using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>A source of prices, such as one market-data API.</summary>
/// <remarks>
/// Implementations return exactly one <see cref="QuoteResult"/> per requested instrument and
/// report data problems (HTTP errors, unknown symbols, malformed payloads) as
/// <see cref="QuoteUnavailable"/>. They throw only on cancellation.
/// </remarks>
public interface IQuoteProvider : IKeyedProvider
{
    Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken);
}
