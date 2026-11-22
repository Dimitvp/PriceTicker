using Microsoft.Extensions.Logging;
using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.History;

/// <summary>Fetches price histories for a mixed list of instruments, each from its own provider.</summary>
public sealed class PriceHistoryService : IPriceHistoryService
{
    private readonly ProviderRouter<IPriceHistoryProvider, HistoryResult> _router;

    public PriceHistoryService(IEnumerable<IPriceHistoryProvider> providers, ILogger<PriceHistoryService> logger)
    {
        _router = new ProviderRouter<IPriceHistoryProvider, HistoryResult>(
            providers,
            (instrument, reason) => new HistoryUnavailable(instrument, reason),
            logger);
    }

    public Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
        IReadOnlyList<Instrument> instruments,
        ChartRange range,
        CancellationToken cancellationToken) =>
        _router.FetchAsync(
            instruments,
            (provider, group) => provider.GetHistoryAsync(group, range, cancellationToken),
            cancellationToken);
}
