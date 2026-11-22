using Microsoft.Extensions.Logging.Abstractions;
using PriceTicker.Core.History;
using PriceTicker.Infrastructure.Yahoo;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class YahooChartHistoryTests
{
    private const string Chart = """
        {
          "chart": {
            "result": [
              {
                "meta": { "currency": "EUR", "regularMarketPrice": 103, "chartPreviousClose": 100, "regularMarketTime": 1763740800 },
                "timestamp": [ 1763700000, 1763703600, 1763707200, 1763710800 ],
                "indicators": { "quote": [ { "close": [ 101.5, null, 102.25, 103 ] } ] }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task GetHistoryAsync_SkipsGapsAndUsesPreviousCloseAsBaseline()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(Chart));

        var results = await provider.GetHistoryAsync([TestInstruments.Create("a", "Yahoo")], ChartRange.Day, CancellationToken.None);

        var history = Assert.IsType<HistoryAvailable>(Assert.Single(results)).History;
        Assert.Equal(new[] { 101.5m, 102.25m, 103m }, history.Points.Select(p => p.Price));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763700000), history.Points[0].Time);
        Assert.Equal(100m, history.Baseline);
        Assert.Equal(ChartRange.Day, history.Range);
    }

    [Theory]
    [InlineData(ChartRange.Day, "range=1d&interval=5m")]
    [InlineData(ChartRange.Week, "range=5d&interval=30m")]
    [InlineData(ChartRange.ThreeMonths, "range=3mo&interval=1d")]
    [InlineData(ChartRange.SixMonths, "range=6mo&interval=1d")]
    [InlineData(ChartRange.Year, "range=1y&interval=1d")]
    public async Task GetHistoryAsync_RequestsRangeAndInterval(ChartRange range, string expectedQuery)
    {
        var handler = StubHttpMessageHandler.Json(Chart);
        var provider = CreateProvider(handler);

        await provider.GetHistoryAsync([TestInstruments.Create("a", "Yahoo", "SXR8.DE")], range, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v8/finance/chart/SXR8.DE", request.RequestUri!.AbsolutePath);
        Assert.Equal("?" + expectedQuery, request.RequestUri.Query);
    }

    [Theory]
    [InlineData("""{ "chart": { "result": [ { "meta": {} } ] } }""")]
    [InlineData("""{ "chart": { "result": [ { "timestamp": [1], "indicators": { "quote": [ { "close": [ null ] } ] } } ] } }""")]
    [InlineData("""{ "chart": { "result": [] } }""")]
    public async Task GetHistoryAsync_WithoutUsablePoints_ReportsNoChartData(string json)
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(json));

        var results = await provider.GetHistoryAsync([TestInstruments.Create("a", "Yahoo")], ChartRange.Week, CancellationToken.None);

        Assert.Equal("No chart data.", Assert.IsType<HistoryUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetHistoryAsync_WithArraysOfDifferentLength_UsesOnlyMatchingPairs()
    {
        const string json = """{ "chart": { "result": [ { "timestamp": [1, 2, 3], "indicators": { "quote": [ { "close": [10, 11] } ] } } ] } }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(json));

        var results = await provider.GetHistoryAsync([TestInstruments.Create("a", "Yahoo")], ChartRange.Week, CancellationToken.None);

        Assert.Equal(2, Assert.IsType<HistoryAvailable>(Assert.Single(results)).History.Points.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_WithNumberTooLargeForDecimal_ReportsUnexpectedResponse()
    {
        const string json = """{ "chart": { "result": [ { "timestamp": [1], "indicators": { "quote": [ { "close": [1e30] } ] } } ] } }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(json));

        var results = await provider.GetHistoryAsync([TestInstruments.Create("a", "Yahoo")], ChartRange.Week, CancellationToken.None);

        Assert.Equal("Unexpected response.", Assert.IsType<HistoryUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetHistoryAsync_WithNetworkFailure_ReportsUnavailable()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throws(new HttpRequestException("offline")));

        var results = await provider.GetHistoryAsync([TestInstruments.Create("a", "Yahoo")], ChartRange.Week, CancellationToken.None);

        Assert.Equal("Network error.", Assert.IsType<HistoryUnavailable>(Assert.Single(results)).Reason);
    }

    private static YahooChartProvider CreateProvider(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler, new Uri("https://yahoo.test/")), NullLogger<YahooChartProvider>.Instance);
}
