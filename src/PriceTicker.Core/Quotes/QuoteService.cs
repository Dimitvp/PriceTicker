using Microsoft.Extensions.Logging;
using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>Fetches quotes for a mixed list of instruments, each from its own provider.</summary>
public sealed class QuoteService : IQuoteService
{
    private readonly ProviderRouter<IQuoteProvider, QuoteResult> _router;

    public QuoteService(IEnumerable<IQuoteProvider> providers, ILogger<QuoteService> logger)
    {
        _router = new ProviderRouter<IQuoteProvider, QuoteResult>(
            providers,
            (instrument, reason) => new QuoteUnavailable(instrument, reason),
            logger);
    }

    public Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken) =>
        _router.FetchAsync(
            instruments,
            (provider, group) => provider.GetQuotesAsync(group, cancellationToken),
            cancellationToken);
}
