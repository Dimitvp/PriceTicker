using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;
using PriceTicker.Infrastructure.TwelveData;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class TwelveDataProviderTests
{
    private const string ApiKey = "test-key-123";

    private static readonly Instrument Gold = TestInstruments.Create("gold", TwelveDataProvider.ProviderKey, "XAU/EUR");
    private static readonly Instrument Bitcoin = TestInstruments.Create("btc", TwelveDataProvider.ProviderKey, "BTC/EUR");

    private const string SingleQuote = """
        { "symbol": "XAU/EUR", "currency_base": "Gold Spot", "currency_quote": "EUR",
          "timestamp": 1763740800, "close": "3512.40", "previous_close": "3500.00" }
        """;

    private const string BatchQuotes = """
        {
          "XAU/EUR": { "symbol": "XAU/EUR", "currency_quote": "EUR", "timestamp": 1763740800, "close": "3512.40", "previous_close": "3500.00" },
          "BTC/EUR": { "code": 404, "message": "symbol not found", "status": "error" }
        }
        """;

    [Fact]
    public async Task GetQuotesAsync_WithoutApiKey_ReportsUnavailableWithoutCallingApi()
    {
        var handler = StubHttpMessageHandler.Json(SingleQuote);
        var provider = CreateProvider(handler, apiKey: null);

        var results = await provider.GetQuotesAsync([Gold], CancellationToken.None);

        Assert.Equal("API key not set.", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetQuotesAsync_SendsKeyInHeader_NotInUrl()
    {
        var handler = StubHttpMessageHandler.Json(SingleQuote);
        var provider = CreateProvider(handler);

        await provider.GetQuotesAsync([Gold], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain(ApiKey, request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal($"apikey {ApiKey}", Assert.Single(request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task GetQuotesAsync_RequestsAllSymbolsInOneCall()
    {
        var handler = StubHttpMessageHandler.Json(BatchQuotes);
        var provider = CreateProvider(handler);

        await provider.GetQuotesAsync([Gold, Bitcoin], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("?symbol=XAU%2FEUR%2CBTC%2FEUR", request.RequestUri!.Query);
    }

    [Fact]
    public async Task GetQuotesAsync_WithSingleSymbol_ParsesFlatQuote()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(SingleQuote));

        var results = await provider.GetQuotesAsync([Gold], CancellationToken.None);

        var quote = Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote;
        Assert.Equal(3512.40m, quote.Price);
        Assert.Equal(3500.00m, quote.PreviousClose);
        Assert.Equal("EUR", quote.Currency);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenCurrencyMissing_TakesItFromPairSymbol()
    {
        const string noCurrency = """{ "symbol": "BTC/EUR", "timestamp": 1763740800, "close": "55468.36" }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(noCurrency));

        var results = await provider.GetQuotesAsync([Bitcoin], CancellationToken.None);

        Assert.Equal("EUR", Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote.Currency);
    }

    [Fact]
    public async Task GetQuotesAsync_ReadsExchangeAndMarketOpenFlag()
    {
        const string quote = """{ "symbol": "BTC/EUR", "exchange": "Binance", "is_market_open": true, "timestamp": 1, "close": "55000" }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(quote));

        var results = await provider.GetQuotesAsync([Bitcoin], CancellationToken.None);

        var market = Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote.Market;
        Assert.Equal(new Market("Binance", MarketState.Open), market);
    }

    [Theory]
    [InlineData("""{ "symbol": "BTC/EUR", "exchange": "Binance", "is_market_open": false, "timestamp": 1, "close": "55000" }""", MarketState.Closed)]
    [InlineData("""{ "symbol": "BTC/EUR", "exchange": "Binance", "timestamp": 1, "close": "55000" }""", MarketState.Unknown)]
    public async Task GetQuotesAsync_WithClosedOrMissingFlag_ReadsMarketState(string quote, MarketState expected)
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(quote));

        var results = await provider.GetQuotesAsync([Bitcoin], CancellationToken.None);

        var market = Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote.Market;
        Assert.Equal(new Market("Binance", expected), market);
    }

    [Fact]
    public async Task GetQuotesAsync_WithBatch_ParsesEachSymbolIndependently()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(BatchQuotes));

        var results = await provider.GetQuotesAsync([Gold, Bitcoin], CancellationToken.None);

        Assert.IsType<QuoteAvailable>(results[0]);
        Assert.Equal("Provider error 404.", Assert.IsType<QuoteUnavailable>(results[1]).Reason);
    }

    [Fact]
    public async Task GetQuotesAsync_WithTopLevelError_ReportsAllUnavailable_WithoutEchoingMessage()
    {
        const string error = """{ "code": 401, "message": "**apikey** test-key-123 is invalid", "status": "error" }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(error));

        var results = await provider.GetQuotesAsync([Gold, Bitcoin], CancellationToken.None);

        Assert.All(results, result =>
        {
            var unavailable = Assert.IsType<QuoteUnavailable>(result);
            Assert.Equal("Provider error 401.", unavailable.Reason);
            Assert.DoesNotContain(ApiKey, unavailable.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GetQuotesAsync_WithSymbolMissingFromBatch_ReportsItUnavailable()
    {
        const string onlyGold = """{ "XAU/EUR": { "currency_quote": "EUR", "timestamp": 1, "close": "1.0" } }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(onlyGold));

        var results = await provider.GetQuotesAsync([Gold, Bitcoin], CancellationToken.None);

        Assert.IsType<QuoteAvailable>(results[0]);
        Assert.IsType<QuoteUnavailable>(results[1]);
    }

    [Theory]
    [InlineData("""{ "close": "-1", "timestamp": 1 }""", "Invalid price.")]
    [InlineData("""{ "close": "1", "timestamp": -5 }""", "No timestamp for price.")]
    [InlineData("""{ "status": 1, "close": "1", "timestamp": 1 }""", null)]
    [InlineData("""[1, 2]""", "Unexpected response.")]
    public async Task GetQuotesAsync_WithOddPayload_NeverThrows(string json, string? expectedReason)
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(json));

        var result = Assert.Single(await provider.GetQuotesAsync([Gold], CancellationToken.None));

        if (expectedReason is not null)
        {
            Assert.Equal(expectedReason, Assert.IsType<QuoteUnavailable>(result).Reason);
        }
    }

    [Fact]
    public async Task GetQuotesAsync_WithHttpError_ReportsAllUnavailable()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json("{}", HttpStatusCode.TooManyRequests));

        var results = await provider.GetQuotesAsync([Gold, Bitcoin], CancellationToken.None);

        Assert.All(results, result => Assert.Equal("HTTP 429", Assert.IsType<QuoteUnavailable>(result).Reason));
    }

    [Fact]
    public async Task GetQuotesAsync_WhenRequestTimesOut_ReportsTimedOut()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new TaskCanceledException("timeout")));

        var results = await provider.GetQuotesAsync([Gold], CancellationToken.None);

        Assert.Equal("Timed out.", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenCallerCancels_Throws()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new TaskCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetQuotesAsync([Gold], new CancellationToken(canceled: true)));
    }

    private static TwelveDataProvider CreateProvider(StubHttpMessageHandler handler, string? apiKey = ApiKey)
    {
        return new TwelveDataProvider(
            new StubHttpClientFactory(handler, new Uri("https://twelvedata.test/")),
            Options.Create(new TwelveDataOptions { ApiKey = apiKey }),
            new TwelveDataCreditMeter(TimeProvider.System),
            NullLogger<TwelveDataProvider>.Instance);
    }
}
