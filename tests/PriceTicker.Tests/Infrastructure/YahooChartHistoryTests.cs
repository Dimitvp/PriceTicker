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

    [Fact]
    public void ToHistory_ForDay_KeepsTheLastTradingDayOfBarsAcrossSessions()
    {
        // Last session's 100 bars, then the first 2 bars after the next open (Yahoo's range=1d is empty then).
        var bars = 120;
        var timestamps = Enumerable.Range(0, bars).Select(i => 1_800_000_000L + (i * 300)).ToList();
        var closes = Enumerable.Range(0, bars).Select(i => (decimal?)(100 + i)).ToList();
        closes[10] = null;

        var history = Day(timestamps, closes);

        var start = bars - YahooChartMapper.DayBars;
        Assert.Equal(YahooChartMapper.DayBars, history.Points.Count);
        Assert.Equal(100m + start, history.Points[0].Price);
        Assert.Equal(100m + bars - 1, history.Points[^1].Price);
        Assert.Equal(100m + start - 1, history.Baseline);
    }

    [Fact]
    public void ToHistory_ForDay_CountsEmptyBarsSoAnIlliquidEtfStillGetsOneDay()
    {
        var bars = 200;
        var timestamps = Enumerable.Range(0, bars).Select(i => 1_800_000_000L + (i * 300)).ToList();
        var closes = Enumerable.Range(0, bars).Select(i => i % 4 == 0 ? (decimal?)(100 + i) : null).ToList();

        var history = Day(timestamps, closes);

        Assert.True(history.Points[0].Time >= DateTimeOffset.FromUnixTimeSeconds(timestamps[bars - YahooChartMapper.DayBars]));
        Assert.InRange(history.Points.Count, (YahooChartMapper.DayBars / 4) - 1, (YahooChartMapper.DayBars / 4) + 1);
    }

    private static PriceHistory Day(IReadOnlyList<long> timestamps, IReadOnlyList<decimal?> closes)
    {
        var chart = new YahooChartResult(
            new YahooChartMeta("EUR", null, 1m, null, null, null, null),
            timestamps,
            new YahooIndicators([new YahooQuoteSeries(closes)]));

        var result = YahooChartMapper.ToHistory(TestInstruments.Create("a", "Yahoo"), ChartRange.Day, chart);

        return Assert.IsType<HistoryAvailable>(result).History;
    }

    [Theory]
    [InlineData(ChartRange.Day, "range=5d&interval=5m")]
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
        new(new StubHttpClientFactory(handler, new Uri("https://yahoo.test/")), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), NullLogger<YahooChartProvider>.Instance);
}
