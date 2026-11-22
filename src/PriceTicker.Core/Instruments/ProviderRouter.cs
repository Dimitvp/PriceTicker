using Microsoft.Extensions.Logging;

namespace PriceTicker.Core.Instruments;

/// <summary>
/// Sends each instrument to the provider named by its <see cref="Instrument.ProviderKey"/>,
/// queries providers in parallel and returns one result per instrument in the caller's order.
/// Shared by the quote and history services so the routing rules live in one place.
/// </summary>
internal sealed class ProviderRouter<TProvider, TResult>
    where TProvider : IKeyedProvider
    where TResult : InstrumentResult
{
    private readonly Dictionary<string, TProvider> _providersByKey;
    private readonly Func<Instrument, string, TResult> _unavailable;
    private readonly ILogger _logger;

    public ProviderRouter(IEnumerable<TProvider> providers, Func<Instrument, string, TResult> unavailable, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(unavailable);
        ArgumentNullException.ThrowIfNull(logger);

        // ToDictionary throws on duplicate keys: two providers claiming one key is a wiring bug.
        _providersByKey = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        _unavailable = unavailable;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TResult>> FetchAsync(
        IReadOnlyList<Instrument> instruments,
        Func<TProvider, IReadOnlyList<Instrument>, Task<IReadOnlyList<TResult>>> fetch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(fetch);

        var fetches = instruments
            .GroupBy(i => i.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => FetchFromProviderAsync(group.Key, [.. group], fetch, cancellationToken));

        var resultsById = (await Task.WhenAll(fetches).ConfigureAwait(false))
            .SelectMany(results => results)
            .DistinctBy(result => result.Instrument.Id)
            .ToDictionary(result => result.Instrument.Id, StringComparer.Ordinal);

        return [.. instruments.Select(instrument =>
            resultsById.GetValueOrDefault(instrument.Id) ?? _unavailable(instrument, "No data returned."))];
    }

    private async Task<IReadOnlyList<TResult>> FetchFromProviderAsync(
        string providerKey,
        IReadOnlyList<Instrument> instruments,
        Func<TProvider, IReadOnlyList<Instrument>, Task<IReadOnlyList<TResult>>> fetch,
        CancellationToken cancellationToken)
    {
        if (!_providersByKey.TryGetValue(providerKey, out var provider))
        {
            ProviderRouterLog.UnknownProvider(_logger, providerKey);
            return MarkAll(instruments, $"Unknown provider '{providerKey}'.");
        }

        try
        {
            return await fetch(provider, instruments).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Last line of defence: providers should not throw, but one faulty provider
            // must not blank out the data coming from the others.
            ProviderRouterLog.ProviderFailed(_logger, ex, providerKey);
            return MarkAll(instruments, "Provider error.");
        }
    }

    private TResult[] MarkAll(IEnumerable<Instrument> instruments, string reason) =>
        [.. instruments.Select(instrument => _unavailable(instrument, reason))];
}

internal static partial class ProviderRouterLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "No provider is registered under key '{ProviderKey}'.")]
    public static partial void UnknownProvider(ILogger logger, string providerKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provider '{ProviderKey}' failed unexpectedly.")]
    public static partial void ProviderFailed(ILogger logger, Exception exception, string providerKey);
}
