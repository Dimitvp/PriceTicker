using Microsoft.Extensions.Logging;
using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.Quotes;

/// <summary>
/// Routes each instrument to the provider named by its <see cref="Instrument.ProviderKey"/>,
/// queries providers in parallel and returns the results in the caller's order.
/// </summary>
public sealed partial class QuoteService : IQuoteService
{
    private readonly Dictionary<string, IQuoteProvider> _providersByKey;
    private readonly ILogger<QuoteService> _logger;

    public QuoteService(IEnumerable<IQuoteProvider> providers, ILogger<QuoteService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        // ToDictionary throws on duplicate keys: two providers claiming one key is a wiring bug.
        _providersByKey = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        var fetches = instruments
            .GroupBy(i => i.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => FetchFromProviderAsync(group.Key, [.. group], cancellationToken));

        var resultsById = (await Task.WhenAll(fetches).ConfigureAwait(false))
            .SelectMany(results => results)
            .DistinctBy(result => result.Instrument.Id)
            .ToDictionary(result => result.Instrument.Id, StringComparer.Ordinal);

        return [.. instruments.Select(instrument =>
            resultsById.GetValueOrDefault(instrument.Id)
            ?? new QuoteUnavailable(instrument, "No data returned."))];
    }

    private async Task<IReadOnlyList<QuoteResult>> FetchFromProviderAsync(
        string providerKey,
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        if (!_providersByKey.TryGetValue(providerKey, out var provider))
        {
            LogUnknownProvider(providerKey);
            return QuoteUnavailable.ForAll(instruments, $"Unknown provider '{providerKey}'.");
        }

        try
        {
            return await provider.GetQuotesAsync(instruments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Last line of defence: providers should not throw, but one faulty provider
            // must not blank out the prices coming from the others.
            LogProviderFailed(ex, providerKey);
            return QuoteUnavailable.ForAll(instruments, "Provider error.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No quote provider is registered under key '{ProviderKey}'.")]
    private partial void LogUnknownProvider(string providerKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Quote provider '{ProviderKey}' failed unexpectedly.")]
    private partial void LogProviderFailed(Exception exception, string providerKey);
}
