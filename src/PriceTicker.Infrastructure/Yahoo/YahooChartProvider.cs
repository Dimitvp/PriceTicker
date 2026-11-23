using System.Text.Json;
using Microsoft.Extensions.Logging;
using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.Yahoo;

/// <summary>
/// Prices instruments and their history through Yahoo Finance's public chart endpoint
/// (<c>/v8/finance/chart</c>), one symbol per request.
/// </summary>
/// <remarks>
/// The endpoint is unofficial and undocumented: it needs no key, covers European exchanges
/// (e.g. <c>SXR8.DE</c>, <c>SMH.MI</c>, <c>DEFS.PA</c>) and quotes are typically delayed.
/// It can change without notice, which is why it sits behind the provider interfaces.
/// </remarks>
public sealed partial class YahooChartProvider : IQuoteProvider, IPriceHistoryProvider
{
    public const string ProviderKey = "Yahoo";
    internal const string HttpClientName = "Yahoo";

    private const string QuoteQuery = "range=1d&interval=1d";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YahooChartProvider> _logger;

    public YahooChartProvider(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<YahooChartProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Key => ProviderKey;

    public Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        // One "now" for the whole batch, so rows from the same exchange agree on open or closed.
        var now = _timeProvider.GetUtcNow();
        return FetchEachAsync<QuoteResult>(
            instruments,
            QuoteQuery,
            (instrument, chart) => YahooChartMapper.ToQuote(instrument, chart, now),
            (instrument, reason) => new QuoteUnavailable(instrument, reason),
            cancellationToken);
    }

    public Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
        IReadOnlyList<Instrument> instruments,
        ChartRange range,
        CancellationToken cancellationToken) =>
        FetchEachAsync<HistoryResult>(
            instruments,
            HistoryQuery(range),
            (instrument, chart) => YahooChartMapper.ToHistory(instrument, range, chart),
            (instrument, reason) => new HistoryUnavailable(instrument, reason),
            cancellationToken);

    // "1D" on Yahoo is the last trading session, which is what a chart of an ETF should show
    // on a weekend; the finer intervals keep short ranges smooth.
    internal static string HistoryQuery(ChartRange range) => range switch
    {
        ChartRange.Day => "range=1d&interval=5m",
        ChartRange.Week => "range=5d&interval=30m",
        ChartRange.ThreeMonths => "range=3mo&interval=1d",
        ChartRange.SixMonths => "range=6mo&interval=1d",
        ChartRange.Year => "range=1y&interval=1d",
        _ => throw new ArgumentOutOfRangeException(nameof(range), range, "Unsupported chart range."),
    };

    private async Task<IReadOnlyList<TResult>> FetchEachAsync<TResult>(
        IReadOnlyList<Instrument> instruments,
        string query,
        Func<Instrument, YahooChartResult?, TResult> map,
        Func<Instrument, string, TResult> unavailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var requests = instruments.Select(instrument =>
            FetchOneAsync(httpClient, instrument, query, map, unavailable, cancellationToken));
        return await Task.WhenAll(requests).ConfigureAwait(false);
    }

    private async Task<TResult> FetchOneAsync<TResult>(
        HttpClient httpClient,
        Instrument instrument,
        string query,
        Func<Instrument, YahooChartResult?, TResult> map,
        Func<Instrument, string, TResult> unavailable,
        CancellationToken cancellationToken)
    {
        var requestUri = $"v8/finance/chart/{Uri.EscapeDataString(instrument.Symbol)}?{query}";

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(instrument.Symbol, (int)response.StatusCode);
                return unavailable(instrument, $"HTTP {(int)response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var payload = await JsonSerializer
                    .DeserializeAsync(stream, YahooJsonContext.Default.YahooChartResponse, cancellationToken)
                    .ConfigureAwait(false);

                return map(instrument, payload?.Chart?.Result is [var first, ..] ? first : null);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException: the connection dropped while the body was being read.
            LogRequestFailed(ex, instrument.Symbol);
            return unavailable(instrument, "Network error.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(ex, instrument.Symbol);
            return unavailable(instrument, "Timed out.");
        }
        catch (JsonException ex)
        {
            LogRequestFailed(ex, instrument.Symbol);
            return unavailable(instrument, "Unexpected response.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo returned HTTP {StatusCode} for {Symbol}.")]
    private partial void LogHttpFailure(string symbol, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo request for {Symbol} failed.")]
    private partial void LogRequestFailed(Exception exception, string symbol);
}
