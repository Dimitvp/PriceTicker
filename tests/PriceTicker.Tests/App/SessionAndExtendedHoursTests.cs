using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotas;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class SessionAndExtendedHoursTests
{
    // Thursday morning before the open; the last session was Wednesday (noon UTC is Wednesday in any zone a test runs in).
    private static readonly DateTimeOffset ThursdayMorning = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WednesdayClose = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Instrument Smh = TestInstruments.Create("smh");

    public SessionAndExtendedHoursTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void DailyChange_BeforeTheOpen_NamesTheDayItIsFrom()
    {
        var item = Row(new Market("XETRA", MarketState.BeforeOpen, ThursdayMorning.AddHours(1)), WednesdayClose);

        Assert.Equal("Wed -2.00 %", item.ChangeText);
        Assert.StartsWith("Change in the last session (Wed", item.ChangeDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void DailyChange_WhileOpen_HasNoDay()
    {
        var item = Row(new Market("XETRA", MarketState.Open), ThursdayMorning);

        Assert.Equal("-2.00 %", item.ChangeText);
    }

    [Fact]
    public void ExtendedHours_IsMeasuredAgainstTheHomePrice_AndShownOnlyWhileTheHomeExchangeIsShut()
    {
        var item = Row(new Market("Milan", MarketState.BeforeOpen, ThursdayMorning.AddHours(1)), WednesdayClose);

        item.ApplyExtendedHours(new QuoteAvailable(new Quote(Smh, 107.1m, 103m, "EUR", ThursdayMorning, new Market("Tradegate", MarketState.Unknown))));

        Assert.Equal("TG +9.29 %", item.ExtendedHoursText);
        Assert.True(item.ShowsExtendedHours);
        Assert.True(item.IsExtendedHoursUp);

        item.Apply(new QuoteAvailable(new Quote(Smh, 98m, 100m, "EUR", ThursdayMorning, new Market("Milan", MarketState.Open))));

        Assert.False(item.ShowsExtendedHours);
        Assert.Equal(string.Empty, item.ExtendedHoursText);
    }

    [Fact]
    public async Task Refresh_ShowsTheDailyQuota()
    {
        var quota = new FakeQuotaSource(new DailyQuota("Twelve Data", 58, 800, ThursdayMorning.AddDays(1)));
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(1m), new FakeTimeProvider(ThursdayMorning), quota: quota, ids: "a");

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("Twelve Data: 742 / 800 left today", viewModel.QuotaText);
        Assert.True(viewModel.HasQuota);
        Assert.False(viewModel.IsQuotaLow);
    }

    [Fact]
    public async Task RefreshOnShow_SkipsFreshPrices_AndRefreshesOldOnes()
    {
        var clock = new FakeTimeProvider(ThursdayMorning);
        var service = FakeQuoteService.PricingEverythingAt(1m);
        var viewModel = TestViewModels.Create(service, clock, ids: "a");
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.RefreshOnShowAsync(CancellationToken.None);
        Assert.Equal(1, service.CallCount);

        clock.Advance(TimeSpan.FromMinutes(2));
        await viewModel.RefreshOnShowAsync(CancellationToken.None);
        Assert.Equal(2, service.CallCount);
    }

    [Fact]
    public async Task Refresh_AsksTheExtendedHoursVenueOnlyForRowsWithAnIsin()
    {
        var extended = FakeExtendedHoursService.TradingNothing();
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(1m), new FakeTimeProvider(ThursdayMorning), extendedHours: extended, ids: "a");

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Empty(Assert.Single(extended.Calls));
    }


    private static QuoteItemViewModel Row(Market market, DateTimeOffset asOf)
    {
        var item = new QuoteItemViewModel(Smh, clock: new FakeTimeProvider(ThursdayMorning));
        item.Apply(new QuoteAvailable(new Quote(Smh, 98m, 100m, "EUR", asOf, market)));
        return item;
    }
}
