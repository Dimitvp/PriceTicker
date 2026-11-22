using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Prices instruments through Twelve Data's official <c>/quote</c> endpoint, all symbols in
/// one request. The free plan covers forex, metals and crypto (e.g. <c>XAU/EUR</c>,
/// <c>BTC/EUR</c>) at 8 credits a minute and 800 a day; each symbol costs one credit.
/// </summary>
public sealed partial class TwelveDataQuoteProvider : IQuoteProvider
{
    public const string ProviderKey = "TwelveData";
    internal const string HttpClientName = "TwelveData";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwelveDataOptions> _options;
    private readonly ILogger<TwelveDataQuoteProvider> _logger;

    public TwelveDataQuoteProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<TwelveDataOptions> options,
        ILogger<TwelveDataQuoteProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
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

        var apiKey = _options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return QuoteUnavailable.ForAll(instruments, "API key not set.");
        }

        var symbols = string.Join(',', instruments.Select(i => i.Symbol));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"quote?symbol={Uri.EscapeDataString(symbols)}");

        // Sent as a header rather than a query parameter so the key never appears in URLs or logs.
        request.Headers.TryAddWithoutValidation("Authorization", $"apikey {apiKey}");

        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure((int)response.StatusCode);
                return QuoteUnavailable.ForAll(instruments, $"HTTP {(int)response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return TwelveDataQuoteParser.Parse(document.RootElement, instruments);
            }
        }
        catch (HttpRequestException ex)
        {
            LogRequestFailed(ex);
            return QuoteUnavailable.ForAll(instruments, "Network error.");
        }
        catch (IOException ex)
        {
            // Connection dropped while the body was being read.
            LogRequestFailed(ex);
            return QuoteUnavailable.ForAll(instruments, "Network error.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(ex);
            return QuoteUnavailable.ForAll(instruments, "Timed out.");
        }
        catch (JsonException ex)
        {
            LogRequestFailed(ex);
            return QuoteUnavailable.ForAll(instruments, "Unexpected response.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twelve Data returned HTTP {StatusCode}.")]
    private partial void LogHttpFailure(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twelve Data request failed.")]
    private partial void LogRequestFailed(Exception exception);
}
