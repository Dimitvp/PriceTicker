using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.Tradegate;

/// <summary>
/// Prices instruments by ISIN on Tradegate (Berlin), which trades 08:00–22:00 CET: before the home
/// exchange opens and after it closes. Uses the public <c>refresh.php</c> endpoint behind the
/// exchange's own quote pages; it is unofficial, needs no key and is real-time, one ISIN per request.
/// </summary>
public sealed partial class TradegateQuoteService : IExtendedHoursQuoteService
{
    internal const string HttpClientName = "Tradegate";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TradegateOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TradegateQuoteService> _logger;

    public TradegateQuoteService(
        IHttpClientFactory httpClientFactory,
        IOptions<TradegateOptions> options,
        TimeProvider timeProvider,
        ILogger<TradegateQuoteService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        if (!_options.Value.Enabled)
        {
            return QuoteUnavailable.ForAll(instruments, "Tradegate is switched off.");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var requests = instruments.Select(instrument => instrument.Isin is { } isin
            ? FetchOneAsync(httpClient, instrument, isin, cancellationToken)
            : Task.FromResult<QuoteResult>(new QuoteUnavailable(instrument, "No ISIN.")));

        return await Task.WhenAll(requests).ConfigureAwait(false);
    }

    private async Task<QuoteResult> FetchOneAsync(
        HttpClient httpClient,
        Instrument instrument,
        Isin isin,
        CancellationToken cancellationToken)
    {
        var requestUri = $"refresh.php?isin={Uri.EscapeDataString(isin.Value)}";

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(isin.Value, (int)response.StatusCode);
                return new QuoteUnavailable(instrument, $"HTTP {(int)response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return TradegateQuoteParser.Parse(document.RootElement, instrument, _timeProvider.GetUtcNow());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            LogRequestFailed(ex, isin.Value);
            return new QuoteUnavailable(instrument, "Network error.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(ex, isin.Value);
            return new QuoteUnavailable(instrument, "Timed out.");
        }
        catch (JsonException)
        {
            // A body that is not JSON most likely means Tradegate does not list the ISIN: expected, not logged.
            return new QuoteUnavailable(instrument, "Not traded on Tradegate.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tradegate returned HTTP {StatusCode} for {Isin}.")]
    private partial void LogHttpFailure(string isin, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tradegate request for {Isin} failed.")]
    private partial void LogRequestFailed(Exception exception, string isin);
}
