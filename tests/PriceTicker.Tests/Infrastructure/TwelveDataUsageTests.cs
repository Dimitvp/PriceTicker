using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.Core.Quotes;
using PriceTicker.Infrastructure.TwelveData;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class TwelveDataUsageTests
{
    private const string Usage = """
        { "timestamp": "2026-11-26 08:00:00", "current_usage": 3, "plan_limit": 8,
          "daily_usage": 120, "plan_daily_limit": 800, "plan_category": "basic" }
        """;

    [Fact]
    public async Task GetDailyQuotaAsync_ReadsTheServersCount_OnlyOnceAnHour()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero));
        var handler = StubHttpMessageHandler.Json(Usage);
        var provider = CreateProvider(handler, new TwelveDataCreditMeter(clock));

        var first = await provider.GetDailyQuotaAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await provider.GetDailyQuotaAsync(CancellationToken.None);

        Assert.Equal(120, first!.Used);
        Assert.Equal(800, first.Limit);
        Assert.Equal(120, second!.Used);
        Assert.EndsWith("/api_usage", Assert.Single(handler.Requests).RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDailyQuotaAsync_WithoutApiKey_IsNull()
    {
        var handler = StubHttpMessageHandler.Json(Usage);
        var provider = CreateProvider(handler, new TwelveDataCreditMeter(TimeProvider.System), apiKey: null);

        Assert.Null(await provider.GetDailyQuotaAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetQuotesAsync_OverTheMinuteAllowance_IsHeldBackWithoutCallingApi()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero));
        var meter = new TwelveDataCreditMeter(clock);
        Assert.Equal(CreditCheck.Reserved, meter.TryReserve(TwelveDataCreditMeter.CreditsPerMinute));
        var handler = StubHttpMessageHandler.Json("{}");
        var provider = CreateProvider(handler, meter);

        var results = await provider.GetQuotesAsync(
            [TestInstruments.Create("btc", TwelveDataProvider.ProviderKey, "BTC/EUR")],
            CancellationToken.None);

        Assert.IsType<QuoteUnavailable>(Assert.Single(results));
        Assert.Empty(handler.Requests);
    }

    private static TwelveDataProvider CreateProvider(StubHttpMessageHandler handler, TwelveDataCreditMeter meter, string? apiKey = "key") =>
        new(
            new StubHttpClientFactory(handler, new Uri("https://twelvedata.test/")),
            Options.Create(new TwelveDataOptions { ApiKey = apiKey }),
            meter,
            NullLogger<TwelveDataProvider>.Instance);
}
