using Microsoft.Extensions.Logging;
using PriceTicker.Core.Bullion;

namespace PriceTicker.Infrastructure.Bullion;

/// <summary>
/// A dealer read from its public product page. The page is fetched like a browser would (no key,
/// no login); a parser specific to the dealer pulls the prices out of the HTML.
/// </summary>
/// <remarks>
/// Product pages are not an API: a redesign of the site can stop the parser matching. That shows
/// up as "Prices not found on page." rather than wrong numbers.
/// </remarks>
public sealed partial class DealerPageDealer : IBullionDealer
{
    internal const string HttpClientName = "Bullion";

    // A dealer never pays more than it charges, and real spreads on bars are a few percent.
    // Anything outside this means the page was misread, and a wrong buyback could fake "target reached".
    private const decimal MinBuybackToBuyRatio = 0.85m;

    private readonly Uri _productUrl;
    private readonly Func<string, DealerPrices?> _parse;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DealerPageDealer> _logger;

    internal DealerPageDealer(
        string name,
        Uri productUrl,
        Func<string, DealerPrices?> parse,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<DealerPageDealer> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(productUrl);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        Name = name;
        _productUrl = productUrl;
        _parse = parse;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Name { get; }

    public async Task<BullionQuoteResult> GetQuoteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.GetAsync(_productUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(Name, (int)response.StatusCode);
                return new BullionQuoteUnavailable(Name, $"HTTP {(int)response.StatusCode}");
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (_parse(html) is not { } prices)
            {
                LogPricesNotFound(Name);
                return new BullionQuoteUnavailable(Name, "Prices not found on page.");
            }

            if (!IsPlausible(prices))
            {
                LogPricesImplausible(Name, prices.BuyPrice, prices.BuybackPrice);
                return new BullionQuoteUnavailable(Name, "Prices look wrong.");
            }

            return new BullionQuoteAvailable(
                new BullionQuote(Name, prices.BuyPrice, prices.BuybackPrice, _timeProvider.GetUtcNow()));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            LogRequestFailed(ex, Name);
            return new BullionQuoteUnavailable(Name, "Network error.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(ex, Name);
            return new BullionQuoteUnavailable(Name, "Timed out.");
        }
    }

    internal static bool IsPlausible(DealerPrices prices) =>
        prices.BuybackPrice <= prices.BuyPrice
        && prices.BuybackPrice >= prices.BuyPrice * MinBuybackToBuyRatio;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dealer}: implausible prices read (buy {Buy}, buyback {Buyback}); ignored.")]
    private partial void LogPricesImplausible(string dealer, decimal buy, decimal buyback);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dealer} returned HTTP {StatusCode}.")]
    private partial void LogHttpFailure(string dealer, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dealer}: prices not found on the product page; the site may have changed.")]
    private partial void LogPricesNotFound(string dealer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dealer} request failed.")]
    private partial void LogRequestFailed(Exception exception, string dealer);
}
