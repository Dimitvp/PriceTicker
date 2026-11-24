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

    [Fact]
    public void DayChart_ShowsTheOfficialDailyChange_NotTheChangeFromTheChartsFirstPoint()
    {
        var instrument = TestInstruments.Create("a");
        var item = new QuoteItemViewModel(instrument);
        item.Apply(new QuoteAvailable(new Quote(instrument, 110m, 100m, "EUR", DateTimeOffset.UnixEpoch)));

        item.ApplyHistory(FakeHistoryService.History(ChartRange.Day, [instrument], 50m, 60m)[0]);

        Assert.Equal("+10.00 %", item.ChangeText);
        Assert.Equal("Change since the previous close", item.ChangeDescription);
    }

    [Fact]
    public void Hovering_ShowsThatPointsPriceAndDate_ThenRestoresLiveValues()
    {
        var (item, _) = ItemWithChart(ChartRange.Year, live: 12m, 10m, 11m, 12m);

        item.HoveredIndex = 1;

        Assert.True(item.IsHovering);
        Assert.Equal("11.00 EUR", item.ShownPriceText);
        Assert.Equal(PointTime(1).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), item.ShownChangeText);

        item.HoveredIndex = null;

        Assert.False(item.IsHovering);
        Assert.Equal("12.00 EUR", item.ShownPriceText);
        Assert.Equal("+20.00 %", item.ShownChangeText);
    }

    [Fact]
    public void Hovering_NotifiesEverythingTheRowBindsTo()
    {
        var (item, _) = ItemWithChart(ChartRange.Year, live: 12m, 10m, 11m, 12m);
        var notified = new List<string?>();
        item.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        item.HoveredIndex = 0;

        Assert.Contains(nameof(QuoteItemViewModel.IsHovering), notified);
        Assert.Contains(nameof(QuoteItemViewModel.ShownPriceText), notified);
        Assert.Contains(nameof(QuoteItemViewModel.ShownChangeText), notified);
    }

    [Fact]
    public void WidestPriceText_CoversLiveAndChartPrices()
    {
        var (item, _) = ItemWithChart(ChartRange.Year, live: 99.8m, 98m, 100.2m, 99m);

        Assert.Equal("100.20 EUR", item.WidestPriceText);
    }

    [Theory]
    [InlineData(ChartRange.Day)]
    [InlineData(ChartRange.Week)]
    public void Hovering_IntradayRange_IncludesTimeOfDay(ChartRange range)
    {
        var (item, _) = ItemWithChart(range, live: 2m, 1m, 2m);

        item.HoveredIndex = 0;

        Assert.Equal(PointTime(0).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture), item.ShownChangeText);
    }

    [Fact]
    public void NewChart_ClearsHover()
    {
        var (item, instrument) = ItemWithChart(ChartRange.Year, live: 2m, 1m, 2m);
        item.HoveredIndex = 1;

        item.ApplyHistory(FakeHistoryService.History(ChartRange.Week, [instrument], 5m, 6m)[0]);

        Assert.Null(item.HoveredIndex);
        Assert.False(item.IsHovering);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void HoveredIndexOutsideChart_ShowsLiveValues(int index)
    {
        var (item, _) = ItemWithChart(ChartRange.Year, live: 12m, 10m, 11m, 12m);

        item.HoveredIndex = index;

        Assert.False(item.IsHovering);
        Assert.Equal("12.00 EUR", item.ShownPriceText);
    }

    private static DateTimeOffset PointTime(int index) => DateTimeOffset.UnixEpoch.AddHours(index).ToLocalTime();

    private static (QuoteItemViewModel Item, PriceTicker.Core.Instruments.Instrument Instrument) ItemWithChart(
        ChartRange range, decimal live, params decimal[] prices)
    {
        var instrument = TestInstruments.Create("a");
        var item = new QuoteItemViewModel(instrument);
        item.Apply(new QuoteAvailable(new Quote(instrument, live, null, "EUR", DateTimeOffset.UnixEpoch)));
        item.ApplyHistory(FakeHistoryService.History(range, [instrument], prices)[0]);
        return (item, instrument);
    }

    private static QuoteItemViewModel Apply(decimal price, decimal? previousClose, string currency = "EUR")
    {
        var instrument = TestInstruments.Create("a");
        var item = new QuoteItemViewModel(instrument);
        item.Apply(new QuoteAvailable(new Quote(instrument, price, previousClose, currency, DateTimeOffset.UnixEpoch)));
        return item;
    }
}
