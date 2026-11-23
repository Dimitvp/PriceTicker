using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.App.Configuration;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.Bullion;
using PriceTicker.Core.Holdings;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class HoldingAndGoldBarTests
{
    private static readonly Holding Smh = new(100m, new PriceAlerts(80m, 60m, 150m));

    public HoldingAndGoldBarTests()
    {
        // CurrentCulture flows with the async context, so this affects only this test.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Row_ShowsPurchasePriceAndGain()
    {
        var item = RowAt(Smh, price: 110m);

        Assert.Equal("Bought 100.00 · +10.00 %", item.HoldingText);
        Assert.False(item.IsReviewAlert);
        Assert.False(item.IsSellAlert);
    }

    [Fact]
    public void Row_AtReviewAlert_IsYellow_AndSaysWhy()
    {
        var item = RowAt(Smh, price: 70m);

        Assert.True(item.IsReviewAlert);
        Assert.False(item.IsSellAlert);
        Assert.StartsWith("Review alert reached", item.AlertDescription, StringComparison.Ordinal);
        Assert.Contains("Exit ≤ 60.00", item.AlertDescription, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(150)]
    public void Row_AtSellAlert_IsRedNotYellow(decimal price)
    {
        var item = RowAt(Smh, price);

        Assert.True(item.IsSellAlert);
        Assert.False(item.IsReviewAlert);
    }

    [Fact]
    public void Row_ShowsPurchasePriceWithoutRoundingIt()
    {
        var item = RowAt(new Holding(4.125m, new PriceAlerts(3.3m, null, null)), price: 4.125m);

        Assert.Equal("Bought 4.125 · 0.00 %", item.HoldingText);
        Assert.Equal("Alerts: Review ≤ 3.30", item.AlertDescription);
    }

    [Fact]
    public async Task SlowGoldBar_DoesNotHoldBackPrices()
    {
        var dealerGate = new TaskCompletionSource<IReadOnlyList<BullionQuoteResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var goldBar = new GoldBarViewModel(new GatedBullionService(dealerGate.Task), Configured());
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), goldBar: goldBar, ids: "a");

        var refresh = viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("7.00 EUR", viewModel.Items[0].PriceText);
        Assert.False(refresh.IsCompleted);
        dealerGate.SetResult([FakeBullionService.Quote("igold", 1030m, 1004m)]);
        await refresh;
        Assert.Equal("1,004.00 EUR", Assert.Single(goldBar.Dealers).BuybackText);
    }

    [Fact]
    public void Row_AlertsCarryATextBadge_NotOnlyAColour()
    {
        Assert.Equal("REVIEW", RowAt(Smh, price: 70m).AlertBadge);
        Assert.Equal("SELL", RowAt(Smh, price: 60m).AlertBadge);
        Assert.Equal(string.Empty, RowAt(Smh, price: 100m).AlertBadge);
    }

    [Theory]
    [InlineData(MarketState.Open, "XETRA · open", true)]
    [InlineData(MarketState.Closed, "XETRA · closed", false)]
    [InlineData(MarketState.Unknown, "XETRA", false)]
    public void Row_ShowsExchangeAndState(MarketState state, string expected, bool expectedOpen)
    {
        var item = RowWithMarket(new Market("XETRA", state));

        Assert.Equal(expected, item.MarketText);
        Assert.Equal(expectedOpen, item.IsMarketOpen);
    }

    [Fact]
    public void Row_BeforeOpenLaterToday_ShowsOnlyTheTime()
    {
        var opensAt = new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
        var clock = ClockAt(opensAt.ToLocalTime().Date.AddHours(1), opensAt.ToLocalTime().Offset);

        var item = RowWithMarket(new Market("XETRA", MarketState.BeforeOpen, opensAt), clock);

        Assert.Equal($"XETRA · opens {opensAt.ToLocalTime():HH:mm}", item.MarketText);
    }

    [Fact]
    public void Row_BeforeOpenOnAnotherDay_ShowsTheDayToo()
    {
        var mondayOpen = new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
        var clock = ClockAt(mondayOpen.ToLocalTime().Date.AddDays(-2).AddHours(12), mondayOpen.ToLocalTime().Offset);

        var item = RowWithMarket(new Market("XETRA", MarketState.BeforeOpen, mondayOpen), clock);

        var expected = mondayOpen.ToLocalTime().ToString("ddd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal($"XETRA · opens {expected}", item.MarketText);
    }

    [Fact]
    public void Row_WhenRefreshFails_KeepsOnlyTheExchange()
    {
        var item = RowWithMarket(new Market("XETRA", MarketState.Open));

        item.MarkUnavailable("Refresh failed.");

        Assert.Equal("XETRA", item.MarketText);
        Assert.False(item.IsMarketOpen);
    }

    [Fact]
    public void Row_WithoutMarketInfo_ShowsNoMarketLine()
    {
        Assert.Equal(string.Empty, RowWithMarket(null).MarketText);
    }

    [Fact]
    public void Row_WithoutHolding_ShowsNothingExtra()
    {
        var instrument = TestInstruments.Create("btc");
        var item = new QuoteItemViewModel(instrument);
        item.Apply(new QuoteAvailable(new Quote(instrument, 55000m, null, "EUR", DateTimeOffset.UnixEpoch)));

        Assert.Equal(string.Empty, item.HoldingText);
        Assert.Null(item.AlertDescription);
        Assert.False(item.IsReviewAlert || item.IsSellAlert);
    }

    [Fact]
    public void TickerRows_GetTheirConfiguredHolding()
    {
        var options = new TickerOptions
        {
            Instruments =
            [
                new InstrumentOptions { Id = "smh", DisplayName = "SMH", Symbol = "SMH.MI", Provider = "Fake" },
                new InstrumentOptions { Id = "btc", DisplayName = "BTC", Symbol = "BTC/EUR", Provider = "Fake" },
            ],
            Holdings = { ["SMH"] = new HoldingOptions { PurchasePrice = 100m, ReviewAtOrBelow = 80m } },
        };
        var viewModel = new TickerViewModel(
            FakeQuoteService.PricingEverythingAt(70m),
            FakeHistoryService.Returning(1m, 2m),
            TestViewModels.NoGoldBar(),
            Options.Create(options),
            new FakeTimeProvider(),
            new FakeAutoStart(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TickerViewModel>.Instance);

        viewModel.RefreshCommand.Execute(null);

        Assert.True(viewModel.Items[0].IsReviewAlert);
        Assert.Equal(string.Empty, viewModel.Items[1].HoldingText);
    }

    [Fact]
    public async Task GoldBar_ShowsDealersPurchaseAndTargets()
    {
        var goldBar = ConfiguredGoldBar(
            FakeBullionService.Quote("Tavex", 1033m, 1003m),
            FakeBullionService.Quote("igold", 1030.34m, 1004.64m));

        await goldBar.RefreshAsync(CancellationToken.None);

        Assert.True(goldBar.IsVisible);
        Assert.Equal("Bought 1,000.00 EUR", goldBar.PurchaseText);
        Assert.Equal(new[] { "+5 %  at buyback ≥ 1,050.00 EUR", "+7 %  at buyback ≥ 1,070.00 EUR" }, goldBar.Targets);
        Assert.Equal(new[] { "Tavex", "igold" }, goldBar.Dealers.Select(d => d.Dealer));
        Assert.Equal(("1,030.34 EUR", "1,004.64 EUR"), (goldBar.Dealers[1].BuyText, goldBar.Dealers[1].BuybackText));
        Assert.False(goldBar.Dealers[1].IsTargetReached);
        Assert.Contains("Selling now: +4.64 EUR (+0.46 %)", goldBar.Dealers[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoldBar_BuybackAtTarget_IsMarkedReached()
    {
        var goldBar = ConfiguredGoldBar(FakeBullionService.Quote("igold", 1080m, 1055m));

        await goldBar.RefreshAsync(CancellationToken.None);

        Assert.True(goldBar.Dealers[0].IsTargetReached);
        Assert.Contains("Target +5 % reached.", goldBar.Dealers[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoldBar_DealerFailure_KeepsLastPricesDimmed()
    {
        var results = new List<BullionQuoteResult> { FakeBullionService.Quote("igold", 1030m, 1004m) };
        var goldBar = new GoldBarViewModel(new MutableBullionService(results), Configured());
        await goldBar.RefreshAsync(CancellationToken.None);

        results[0] = new BullionQuoteUnavailable("igold", "HTTP 403");
        await goldBar.RefreshAsync(CancellationToken.None);

        var row = Assert.Single(goldBar.Dealers);
        Assert.Equal("1,004.00 EUR", row.BuybackText);
        Assert.True(row.IsStale);
        Assert.Equal("HTTP 403", row.Detail);
    }

    [Fact]
    public async Task GoldBar_NotConfigured_IsHiddenAndNeverCallsDealers()
    {
        var service = new FakeBullionService([]);
        var goldBar = new GoldBarViewModel(service, Options.Create(new GoldBarOptions()));

        await goldBar.RefreshAsync(CancellationToken.None);

        Assert.False(goldBar.IsVisible);
        Assert.Equal(0, service.CallCount);
    }

    [Theory]
    [InlineData("", 0, new double[0], true)]
    [InlineData("Bar", 1000, new double[] { 5, 7 }, true)]
    [InlineData("Bar", 0, new double[] { 5 }, false)]
    [InlineData("Bar", 1000, new double[0], false)]
    [InlineData("Bar", 1000, new double[] { -5 }, false)]
    [InlineData("", 1000, new double[] { 5 }, false)]
    public void GoldBarOptions_Validation(string name, double price, double[] targets, bool expectedValid)
    {
        var options = new GoldBarOptions { Name = name, PurchasePrice = (decimal)price, ProfitTargetsPercent = [.. targets.Select(t => (decimal)t)] };

        Assert.Equal(expectedValid, options.IsValid);
    }

    private static FakeTimeProvider ClockAt(DateTime localTime, TimeSpan offset) =>
        new(new DateTimeOffset(localTime, offset));

    private static QuoteItemViewModel RowWithMarket(Market? market, TimeProvider? clock = null)
    {
        var instrument = TestInstruments.Create("sxr8");
        var item = new QuoteItemViewModel(instrument, clock: clock ?? new FakeTimeProvider());
        item.Apply(new QuoteAvailable(new Quote(instrument, 700m, null, "EUR", DateTimeOffset.UnixEpoch, market)));
        return item;
    }

    private static QuoteItemViewModel RowAt(Holding holding, decimal price)
    {
        var instrument = TestInstruments.Create("smh");
        var item = new QuoteItemViewModel(instrument, holding);
        item.Apply(new QuoteAvailable(new Quote(instrument, price, null, "EUR", DateTimeOffset.UnixEpoch)));
        return item;
    }

    private static IOptions<GoldBarOptions> Configured() =>
        Options.Create(new GoldBarOptions { Name = "Gold bar · Valcambi 10 g", PurchasePrice = 1000m, ProfitTargetsPercent = [5m, 7m] });

    private static GoldBarViewModel ConfiguredGoldBar(params BullionQuoteResult[] results) =>
        new(new FakeBullionService(results), Configured());

    private sealed class GatedBullionService(Task<IReadOnlyList<BullionQuoteResult>> gate) : IBullionService
    {
        public Task<IReadOnlyList<BullionQuoteResult>> GetQuotesAsync(CancellationToken cancellationToken) => gate;
    }

    private sealed class MutableBullionService(List<BullionQuoteResult> results) : IBullionService
    {
        public Task<IReadOnlyList<BullionQuoteResult>> GetQuotesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BullionQuoteResult>>([.. results]);
    }
}
