using System.Text.Json;
using Microsoft.Extensions.Logging;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.Yahoo;

/// <summary>
/// Prices instruments through Yahoo Finance's public chart endpoint (<c>/v8/finance/chart</c>).
/// </summary>
/// <remarks>
/// The endpoint is unofficial and undocumented: it needs no key, covers European exchanges
/// (e.g. <c>SXR8.DE</c>, <c>SMH.MI</c>, <c>DEFS.PA</c>) and quotes are typically delayed.
/// It can change without notice, which is why it sits behind <see cref="IQuoteProvider"/>.
/// </remarks>
public sealed partial class YahooChartQuoteProvider : IQuoteProvider
{
    public const string ProviderKey = "Yahoo";
    internal const string HttpClientName = "Yahoo";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YahooChartQuoteProvider> _logger;

    public YahooChartQuoteProvider(IHttpClientFactory httpClientFactory, ILogger<YahooChartQuoteProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        // The chart endpoint serves one symbol per request.
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var requests = instruments.Select(instrument => GetQuoteAsync(httpClient, instrument, cancellationToken));
        return await Task.WhenAll(requests).ConfigureAwait(false);
    }

    private async Task<QuoteResult> GetQuoteAsync(
        HttpClient httpClient,
        Instrument instrument,
        CancellationToken cancellationToken)
    {
        var requestUri = $"v8/finance/chart/{Uri.EscapeDataString(instrument.Symbol)}?range=1d&interval=1d";

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(instrument.Symbol, (int)response.StatusCode);
                return new QuoteUnavailable(instrument, $"HTTP {(int)response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var payload = await JsonSerializer
                    .DeserializeAsync(stream, YahooJsonContext.Default.YahooChartResponse, cancellationToken)
                    .ConfigureAwait(false);

                return ToResult(instrument, payload);
            }
        }
        catch (HttpRequestException ex)
        {
            LogRequestFailed(ex, instrument.Symbol);
            return new QuoteUnavailable(instrument, "Network error.");
        }
        catch (IOException ex)
        {
            // Connection dropped while the body was being read.
            LogRequestFailed(ex, instrument.Symbol);
            return new QuoteUnavailable(instrument, "Network error.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(ex, instrument.Symbol);
            return new QuoteUnavailable(instrument, "Timed out.");
        }
        catch (JsonException ex)
        {
            LogRequestFailed(ex, instrument.Symbol);
            return new QuoteUnavailable(instrument, "Unexpected response.");
        }
    }

    private static QuoteResult ToResult(Instrument instrument, YahooChartResponse? payload)
    {
        var meta = payload?.Chart?.Result is [var first, ..] ? first.Meta : null;
        if (meta?.RegularMarketPrice is not { } price)
        {
            return new QuoteUnavailable(instrument, "No price for symbol.");
        }

        if (price < 0m)
        {
            return new QuoteUnavailable(instrument, "Invalid price.");
        }

        if (meta.RegularMarketTime is not { } unixSeconds || !UnixTime.TryFromSeconds(unixSeconds, out var asOf))
        {
            return new QuoteUnavailable(instrument, "No timestamp for price.");
        }

        var quote = new Quote(
            instrument,
            price,
            meta.ChartPreviousClose ?? meta.PreviousClose,
            meta.Currency ?? string.Empty,
            asOf);

        return new QuoteAvailable(quote);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo returned HTTP {StatusCode} for {Symbol}.")]
    private partial void LogHttpFailure(string symbol, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo request for {Symbol} failed.")]
    private partial void LogRequestFailed(Exception exception, string symbol);
}
