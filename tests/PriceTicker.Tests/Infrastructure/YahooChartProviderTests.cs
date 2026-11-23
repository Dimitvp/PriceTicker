using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.Core.Quotes;
using PriceTicker.Infrastructure.Yahoo;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class YahooChartProviderTests
{
    private const string ValidChart = """
        {
          "chart": {
            "result": [
              {
                "meta": {
                  "currency": "EUR",
                  "symbol": "SXR8.DE",
                  "regularMarketPrice": 684.96,
                  "chartPreviousClose": 680.00,
                  "regularMarketTime": 1763740800
                }
              }
            ],
            "error": null
          }
        }
        """;

    [Fact]
    public async Task GetQuotesAsync_WithValidChart_ReturnsQuoteFromMeta()
    {
        var instrument = TestInstruments.Create("sxr8", YahooChartProvider.ProviderKey, "SXR8.DE");
        var provider = CreateProvider(StubHttpMessageHandler.Json(ValidChart));

        var results = await provider.GetQuotesAsync([instrument], CancellationToken.None);

        var quote = Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote;
        Assert.Equal(684.96m, quote.Price);
        Assert.Equal(680.00m, quote.PreviousClose);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763740800), quote.AsOf);
    }

    [Theory]
    [InlineData(1795415650, MarketState.BeforeOpen)]
    [InlineData(1795425000, MarketState.Open)]
    [InlineData(1795460000, MarketState.Closed)]
    public async Task GetQuotesAsync_ReadsExchangeAndSessionState(long nowUnixSeconds, MarketState expectedState)
    {
        // Real meta of SMH.MI on 23.11.2026 (regular session 1795420800–1795451400).
        const string chart = """
            { "chart": { "result": [ { "meta": { "currency": "EUR", "regularMarketPrice": 70.34, "regularMarketTime": 1795192737,
              "fullExchangeName": "Milan", "exchangeName": "MIL",
              "currentTradingPeriod": { "regular": { "timezone": "CET", "start": 1795420800, "end": 1795451400, "gmtoffset": 3600 } } } } ] } }
            """;
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(nowUnixSeconds));
        var provider = new YahooChartProvider(
            new StubHttpClientFactory(StubHttpMessageHandler.Json(chart), new Uri("https://yahoo.test/")),
            clock,
            NullLogger<YahooChartProvider>.Instance);

        var results = await provider.GetQuotesAsync([TestInstruments.Create("smh", "Yahoo", "SMH.MI")], CancellationToken.None);

        var market = Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote.Market;
        Assert.NotNull(market);
        Assert.Equal("Milan", market.Exchange);
        Assert.Equal(expectedState, market.State);
    }

    [Fact]
    public async Task GetQuotesAsync_WithoutSessionInfo_HasNoMarket()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(ValidChart));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("a", "Yahoo")], CancellationToken.None);

        Assert.Null(Assert.IsType<QuoteAvailable>(Assert.Single(results)).Quote.Market);
    }

    [Fact]
    public async Task GetQuotesAsync_EscapesSymbolInRequestPath()
    {
        var handler = StubHttpMessageHandler.Json(ValidChart);
        var provider = CreateProvider(handler);

        await provider.GetQuotesAsync([TestInstruments.Create("gold", "Yahoo", "GC=F")], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/v8/finance/chart/GC%3DF", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuotesAsync_WithHttpError_ReportsStatusCode()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json("{}", HttpStatusCode.NotFound));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], CancellationToken.None);

        Assert.Equal("HTTP 404", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
    }

    [Theory]
    [InlineData("""{ "chart": { "result": [] } }""")]
    [InlineData("""{ "chart": { "result": [ { "meta": { "currency": "EUR", "regularMarketTime": 1 } } ] } }""")]
    [InlineData("""{ "chart": { "result": [ { "meta": { "regularMarketPrice": 1.5 } } ] } }""")]
    [InlineData("""{ "chart": { "result": [ { "meta": { "regularMarketPrice": -1, "regularMarketTime": 1 } } ] } }""")]
    [InlineData("""{ "chart": { "result": [ { "meta": { "regularMarketPrice": 1, "regularMarketTime": 999999999999999 } } ] } }""")]
    public async Task GetQuotesAsync_WithIncompleteChart_ReportsUnavailable(string json)
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(json));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], CancellationToken.None);

        Assert.IsType<QuoteUnavailable>(Assert.Single(results));
    }

    [Fact]
    public async Task GetQuotesAsync_WithMalformedJson_ReportsUnavailable()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json("not json"));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], CancellationToken.None);

        Assert.Equal("Unexpected response.", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetQuotesAsync_WithNetworkFailure_ReportsUnavailable()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new HttpRequestException("offline")));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], CancellationToken.None);

        Assert.Equal("Network error.", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetQuotesAsync_SendsOneRequestPerInstrument()
    {
        var handler = StubHttpMessageHandler.Json(ValidChart);
        var provider = CreateProvider(handler);

        var results = await provider.GetQuotesAsync(
            [TestInstruments.Create("a", "Yahoo"), TestInstruments.Create("b", "Yahoo")],
            CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenRequestTimesOut_ReportsTimedOut()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new TaskCanceledException("timeout")));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], CancellationToken.None);

        Assert.Equal("Timed out.", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenCallerCancels_Throws()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new TaskCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task GetQuotesAsync_WhenConnectionDropsMidBody_ReportsNetworkError()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new IOException("reset")));

        var results = await provider.GetQuotesAsync([TestInstruments.Create("x", "Yahoo")], CancellationToken.None);

        Assert.Equal("Network error.", Assert.IsType<QuoteUnavailable>(Assert.Single(results)).Reason);
    }

    private static YahooChartProvider CreateProvider(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler, new Uri("https://yahoo.test/")), new FakeTimeProvider(), NullLogger<YahooChartProvider>.Instance);
}
