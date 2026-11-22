using System.Globalization;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.History;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class QuoteItemViewModelTests
{
    public QuoteItemViewModelTests()
    {
        // CurrentCulture flows with the async context, so this affects only this test.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Apply_WithRisingQuote_ShowsPriceChangeAndUpTrend()
    {
        var item = Apply(price: 1234.5m, previousClose: 1200m);

        Assert.Equal("1,234.50 EUR", item.PriceText);
        Assert.Equal("+2.88 %", item.ChangeText);
        Assert.True(item.IsUp);
        Assert.False(item.IsDown);
        Assert.False(item.IsStale);
    }

    [Fact]
    public void Apply_WithFallingQuote_ShowsDownTrend()
    {
        var item = Apply(price: 90m, previousClose: 100m);

        Assert.Equal("-10.00 %", item.ChangeText);
        Assert.True(item.IsDown);
        Assert.False(item.IsUp);
    }

    [Fact]
    public void Apply_WithUnchangedQuote_IsNeitherUpNorDown()
    {
        var item = Apply(price: 100m, previousClose: 100m);

        Assert.Equal("0.00 %", item.ChangeText);
        Assert.False(item.IsUp);
        Assert.False(item.IsDown);
    }

    [Fact]
    public void Apply_WithoutPreviousClose_ShowsNoChange()
    {
        var item = Apply(price: 100m, previousClose: null);

        Assert.Equal(string.Empty, item.ChangeText);
        Assert.False(item.IsUp);
        Assert.False(item.IsDown);
    }

    [Fact]
    public void Apply_WithoutCurrency_ShowsBarePrice()
    {
        var item = Apply(price: 5m, previousClose: null, currency: string.Empty);

        Assert.Equal("5.00", item.PriceText);
    }

    [Fact]
    public void Apply_UnavailableAfterPrice_KeepsPriceAndMarksStale()
    {
        var item = Apply(price: 10m, previousClose: null);

        item.Apply(new QuoteUnavailable(TestInstruments.Create("a"), "Network error."));

        Assert.Equal("10.00 EUR", item.PriceText);
        Assert.True(item.IsStale);
        Assert.Equal("Network error.", item.Detail);
    }

    [Fact]
    public void Apply_PriceAfterStale_ClearsStale()
    {
        var item = Apply(price: 10m, previousClose: null);
        item.MarkUnavailable("Network error.");

        item.Apply(new QuoteAvailable(new Quote(TestInstruments.Create("a"), 11m, null, "EUR", DateTimeOffset.UnixEpoch)));

        Assert.False(item.IsStale);
        Assert.Equal("11.00 EUR", item.PriceText);
    }

    [Fact]
    public void Apply_UnavailableWithoutEarlierPrice_IsNotStale()
    {
        var item = new QuoteItemViewModel(TestInstruments.Create("a"));

        item.Apply(new QuoteUnavailable(TestInstruments.Create("a"), "API key not set."));

        Assert.Equal("—", item.PriceText);
        Assert.False(item.IsStale);
        Assert.Equal("API key not set.", item.Detail);
    }

    [Fact]
    public void ApplyHistory_UnavailableAfterChart_ClearsChartAndFallsBackToDailyChange()
    {
        var instrument = TestInstruments.Create("a");
        var item = new QuoteItemViewModel(instrument);
        item.Apply(new QuoteAvailable(new Quote(instrument, 110m, 100m, "EUR", DateTimeOffset.UnixEpoch)));
        item.ApplyHistory(FakeHistoryService.History(ChartRange.Year, [instrument], 50m, 60m)[0]);
        Assert.Equal("+120.00 %", item.ChangeText);

        item.ApplyHistory(new HistoryUnavailable(instrument, "No chart data."));

        Assert.Empty(item.ChartValues);
        Assert.Equal("No chart data.", item.ChartStatus);
        Assert.Equal("+10.00 %", item.ChangeText);
        Assert.Equal("Daily change (chart unavailable)", item.ChangeDescription);
    }

    private static QuoteItemViewModel Apply(decimal price, decimal? previousClose, string currency = "EUR")
    {
        var instrument = TestInstruments.Create("a");
        var item = new QuoteItemViewModel(instrument);
        item.Apply(new QuoteAvailable(new Quote(instrument, price, previousClose, currency, DateTimeOffset.UnixEpoch)));
        return item;
    }
}
