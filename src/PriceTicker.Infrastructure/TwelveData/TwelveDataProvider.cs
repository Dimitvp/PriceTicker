using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotas;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Prices instruments through Twelve Data's official API: <c>/quote</c> for all symbols in one
/// request, <c>/time_series</c> per symbol for charts. The free plan covers forex and crypto
/// (e.g. <c>BTC/EUR</c>; commodities need a paid plan) at 8 credits a minute and 800 a day,
/// one credit per symbol per request. Every request is counted by <see cref="TwelveDataCreditMeter"/>,
/// which refuses one that would exceed either allowance, so the API never has to say 429.
/// </summary>
public sealed partial class TwelveDataProvider : IQuoteProvider, IPriceHistoryProvider, IDailyQuotaSource
{
    /// <summary>How often the server's own count is read (one credit each time): 24 credits a day.</summary>
    internal static readonly TimeSpan UsageSyncInterval = TimeSpan.FromHours(1);

    public const string ProviderKey = "TwelveData";
    internal const string HttpClientName = "TwelveData";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwelveDataOptions> _options;
    private readonly TwelveDataCreditMeter _meter;
    private readonly ILogger<TwelveDataProvider> _logger;

    public TwelveDataProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<TwelveDataOptions> options,
        TwelveDataCreditMeter meter,
        ILogger<TwelveDataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _meter = meter;
        _logger = logger;
    }

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        if (instruments.Count == 0)
        {
            return [];
        }

        var symbols = string.Join(',', instruments.Select(i => i.Symbol));
        return await SendAsync(
            $"quote?symbol={Uri.EscapeDataString(symbols)}",
            instruments.Count,
            root => TwelveDataQuoteParser.Parse(root, instruments),
            reason => QuoteUnavailable.ForAll(instruments, reason),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
        IReadOnlyList<Instrument> instruments,
        ChartRange range,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        var (interval, outputSize) = SeriesShape(range);
        var requests = instruments.Select(instrument => SendAsync<HistoryResult>(
            $"time_series?symbol={Uri.EscapeDataString(instrument.Symbol)}&interval={interval}&outputsize={outputSize}&timezone=UTC",
            1,
            root => TwelveDataSeriesParser.Parse(root, instrument, range),
            reason => new HistoryUnavailable(instrument, reason),
            cancellationToken));

        return await Task.WhenAll(requests).ConfigureAwait(false);
    }

    /// <summary>
    /// Today's credits. The server's count is read at most once an hour (it costs a credit, and
    /// includes credits spent elsewhere with the same key); in between, the local count is kept.
    /// </summary>
    public async Task<DailyQuota?> GetDailyQuotaAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.ApiKey))
        {
            return null;
        }

        if (_meter.NeedsSynchronising(UsageSyncInterval))
        {
            var usage = await SendAsync<(int Used, int Limit)?>(
                "api_usage",
                1,
                TwelveDataUsageParser.Parse,
                _ => null,
                cancellationToken).ConfigureAwait(false);

            if (usage is { } server)
            {
                _meter.Synchronise(server.Used, server.Limit);
            }
        }

        return _meter.Snapshot();
    }

    // Interval and point count that span the range; Day is a true 24 hours (crypto never closes).
    internal static (string Interval, int OutputSize) SeriesShape(ChartRange range) => range switch
    {
        ChartRange.Day => ("15min", 96),
        ChartRange.Week => ("1h", 168),
        ChartRange.ThreeMonths => ("1day", 92),
        ChartRange.SixMonths => ("1day", 183),
        ChartRange.Year => ("1day", 365),
        _ => throw new ArgumentOutOfRangeException(nameof(range), range, "Unsupported chart range."),
    };

    private async Task<T> SendAsync<T>(
        string requestUri,
        int credits,
        Func<JsonElement, T> parse,
        Func<string, T> unavailable,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return unavailable("API key not set.");
        }

        switch (_meter.TryReserve(credits))
        {
            case CreditCheck.Reserved:
                break;
            case CreditCheck.MinuteLimit:
                LogCreditsHeldBack(credits, "minute");
                return unavailable("Twelve Data: 8 requests a minute used; next refresh.");
            case CreditCheck.DailyLimit:
                LogCreditsHeldBack(credits, "day");
                return unavailable("Twelve Data: today's credits are used up (reset 00:00 UTC).");
            default:
                throw new InvalidOperationException("Unknown credit check result.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        // Sent as a header rather than a query parameter so the key never appears in URLs or logs.
        request.Headers.TryAddWithoutValidation("Authorization", $"apikey {apiKey}");

        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure((int)response.StatusCode);
                return unavailable($"HTTP {(int)response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return parse(document.RootElement);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException: the connection dropped while the body was being read.
            LogRequestFailed(ex);
            return unavailable("Network error.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(ex);
            return unavailable("Timed out.");
        }
        catch (JsonException ex)
        {
            LogRequestFailed(ex);
            return unavailable("Unexpected response.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twelve Data returned HTTP {StatusCode}.")]
    private partial void LogHttpFailure(int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Held back a Twelve Data request for {Credits} credit(s): the allowance for the {Period} is used up.")]
    private partial void LogCreditsHeldBack(int credits, string period);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twelve Data request failed.")]
    private partial void LogRequestFailed(Exception exception);
}
