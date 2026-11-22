using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using PriceTicker.Infrastructure.TwelveData;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class TwelveDataHistoryTests
{
    private static readonly Instrument Bitcoin = TestInstruments.Create("btc", TwelveDataProvider.ProviderKey, "BTC/EUR");

    private const string Series = """
        {
          "meta": { "symbol": "BTC/EUR", "interval": "15min" },
          "values": [
            { "datetime": "2026-11-22 19:45:00", "close": "55468.36" },
            { "datetime": "2026-11-22 19:30:00", "close": "55100.00" },
            { "datetime": "2026-11-22 19:15:00", "close": "not a number" }
          ],
          "status": "ok"
        }
        """;

    [Fact]
    public async Task GetHistoryAsync_ParsesNewestFirstValuesIntoOldestFirstPoints()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Json(Series));

        var results = await provider.GetHistoryAsync([Bitcoin], ChartRange.Day, CancellationToken.None);

        var history = Assert.IsType<HistoryAvailable>(Assert.Single(results)).History;
        Assert.Equal(new[] { 55100.00m, 55468.36m }, history.Points.Select(p => p.Price));
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 19, 30, 0, TimeSpan.Zero), history.Points[0].Time);
        Assert.Equal(55100.00m, history.Baseline);
    }

    [Fact]
    public async Task GetHistoryAsync_ParsesDailyDates()
    {
        const string daily = """{ "values": [ { "datetime": "2026-11-21", "close": "1.5" } ], "status": "ok" }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(daily));

        var results = await provider.GetHistoryAsync([Bitcoin], ChartRange.Year, CancellationToken.None);

        var point = Assert.Single(Assert.IsType<HistoryAvailable>(Assert.Single(results)).History.Points);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), point.Time);
    }

    [Theory]
    [InlineData(ChartRange.Day, "15min", 96)]
    [InlineData(ChartRange.Week, "1h", 168)]
    [InlineData(ChartRange.ThreeMonths, "1day", 92)]
    [InlineData(ChartRange.SixMonths, "1day", 183)]
    [InlineData(ChartRange.Year, "1day", 365)]
    public async Task GetHistoryAsync_RequestsIntervalAndSizeInUtc_WithKeyInHeader(ChartRange range, string interval, int size)
    {
        var handler = StubHttpMessageHandler.Json(Series);
        var provider = CreateProvider(handler);

        await provider.GetHistoryAsync([Bitcoin], range, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"?symbol=BTC%2FEUR&interval={interval}&outputsize={size}&timezone=UTC", request.RequestUri!.Query);
        Assert.Equal("apikey key", Assert.Single(request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task GetHistoryAsync_WithErrorObject_ReportsCodeOnly()
    {
        const string error = """{ "code": 429, "message": "too many requests for key", "status": "error" }""";
        var provider = CreateProvider(StubHttpMessageHandler.Json(error));

        var results = await provider.GetHistoryAsync([Bitcoin], ChartRange.Week, CancellationToken.None);

        Assert.Equal("Provider error 429.", Assert.IsType<HistoryUnavailable>(Assert.Single(results)).Reason);
    }

    [Fact]
    public async Task GetHistoryAsync_WithoutApiKey_DoesNotCallApi()
    {
        var handler = StubHttpMessageHandler.Json(Series);
        var provider = CreateProvider(handler, apiKey: null);

        var results = await provider.GetHistoryAsync([Bitcoin], ChartRange.Week, CancellationToken.None);

        Assert.Equal("API key not set.", Assert.IsType<HistoryUnavailable>(Assert.Single(results)).Reason);
        Assert.Empty(handler.Requests);
    }

    private static TwelveDataProvider CreateProvider(StubHttpMessageHandler handler, string? apiKey = "key") =>
        new(
            new StubHttpClientFactory(handler, new Uri("https://twelvedata.test/")),
            Options.Create(new TwelveDataOptions { ApiKey = apiKey }),
            NullLogger<TwelveDataProvider>.Instance);
}
