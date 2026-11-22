using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.History;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class TickerViewModelChartTests
{
    public TickerViewModelChartTests()
    {
        // CurrentCulture flows with the async context, so this affects only this test.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void SelectedRange_DefaultsToOneDay()
    {
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(6m), new FakeTimeProvider(), ids: "a");

        Assert.Equal(ChartRange.Day, viewModel.SelectedRange.Range);
        Assert.Equal(new[] { "1D", "1W", "3M", "6M", "1Y" }, TickerViewModel.Ranges.Select(r => r.Label));
    }

    [Fact]
    public async Task RefreshAsync_LoadsChartForSelectedRange_AndShowsChangeOverIt()
    {
        var history = FakeHistoryService.Returning(5m, 5.5m);
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(6m), new FakeTimeProvider(), history: history, ids: "a");

        await viewModel.RefreshAsync(CancellationToken.None);

        var item = viewModel.Items[0];
        Assert.Equal(new[] { 5d, 5.5d }, item.ChartValues);
        Assert.Equal("+20.00 %", item.ChangeText);
        Assert.True(item.IsUp);
        Assert.Equal(ChartRange.Day, Assert.Single(history.RequestedRanges));
    }

    [Theory]
    [InlineData(ChartRange.Day)]
    [InlineData(ChartRange.Week)]
    public async Task RefreshAsync_ShortRange_RefetchesChartEveryTime(ChartRange range)
    {
        var history = FakeHistoryService.Returning(1m, 2m);
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(2m), new FakeTimeProvider(), history: history, ids: "a");
        await SelectAndWaitAsync(viewModel, history, range);

        await viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(3, history.RequestedRanges.Count(r => r == range));
    }

    [Fact]
    public async Task RefreshAsync_LongRange_RefetchesChartOnlyOnceItIsAnHourOld()
    {
        var clock = new FakeTimeProvider();
        var history = FakeHistoryService.Returning(1m, 2m);
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(2m), clock, history: history, ids: "a");
        await SelectAndWaitAsync(viewModel, history, ChartRange.Year);

        clock.Advance(TimeSpan.FromMinutes(59));
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, history.RequestedRanges.Count(r => r == ChartRange.Year));

        clock.Advance(TimeSpan.FromMinutes(1));
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, history.RequestedRanges.Count(r => r == ChartRange.Year));
    }

    [Fact]
    public async Task SelectingRange_LoadsChartsForThatRange()
    {
        var history = FakeHistoryService.Returning(10m, 12m);
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(9m), new FakeTimeProvider(), history: history, ids: "a");
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.SelectedRange = Range(ChartRange.Year);
        await WaitUntilAsync(() => viewModel.Items[0].ChangeText == "-10.00 %");

        Assert.Equal(ChartRange.Year, history.RequestedRanges[^1]);
    }

    [Fact]
    public async Task NewRange_CancelsSlowerPreviousLoad_AndShowsOnlyTheNewChart()
    {
        var weekToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = new FakeHistoryService(async (range, instruments, cancellationToken) =>
        {
            if (range != ChartRange.Week)
            {
                return FakeHistoryService.History(range, instruments, 1m, 2m);
            }

            weekToken.SetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeHistoryService.History(range, instruments, 9m, 9m, 9m);
        });
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(2m), new FakeTimeProvider(), history: history, ids: "a");

        viewModel.SelectedRange = Range(ChartRange.Week);
        var token = await weekToken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.SelectedRange = Range(ChartRange.Year);
        await WaitUntilAsync(() => viewModel.Items[0].ChartValues.Count == 2);

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(new[] { 1d, 2d }, viewModel.Items[0].ChartValues);
    }

    [Fact]
    public async Task RangeChangedDuringRefresh_DropsTheRefreshsChart()
    {
        var dayGate = new TaskCompletionSource<IReadOnlyList<HistoryResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = new FakeHistoryService((range, instruments) => range == ChartRange.Day
            ? dayGate.Task
            : Task.FromResult(FakeHistoryService.History(range, instruments, 1m, 2m)));
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(2m), new FakeTimeProvider(), history: history, ids: "a");

        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedRange = Range(ChartRange.Year);
        await WaitUntilAsync(() => viewModel.Items[0].ChartValues.Count == 2);
        dayGate.SetResult(FakeHistoryService.History(ChartRange.Day, [TestInstruments.Create("a")], 7m, 7m, 7m));
        await refresh;

        Assert.Equal(new[] { 1d, 2d }, viewModel.Items[0].ChartValues);
    }

    [Fact]
    public async Task SwitchingBackToARecentRange_ReusesItsChartWithoutARequest()
    {
        var history = FakeHistoryService.Returning(1m, 2m);
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(2m), new FakeTimeProvider(), history: history, ids: "a");
        await SelectAndWaitAsync(viewModel, history, ChartRange.Year);
        await SelectAndWaitAsync(viewModel, history, ChartRange.ThreeMonths);
        var requestsBefore = history.RequestedRanges.Count;

        viewModel.SelectedRange = Range(ChartRange.Year);

        Assert.Equal(requestsBefore, history.RequestedRanges.Count);
        Assert.Equal(new[] { 1d, 2d }, viewModel.Items[0].ChartValues);
    }

    [Fact]
    public async Task SwitchingBackToAnExpiredRange_RequestsItAgain()
    {
        var clock = new FakeTimeProvider();
        var history = FakeHistoryService.Returning(1m, 2m);
        var viewModel = TestViewModels.Create(FakeQuoteService.PricingEverythingAt(2m), clock, history: history, ids: "a");
        await SelectAndWaitAsync(viewModel, history, ChartRange.Year);
        await SelectAndWaitAsync(viewModel, history, ChartRange.ThreeMonths);

        clock.Advance(TickerViewModel.LongRangeChartMaxAge);
        await SelectAndWaitAsync(viewModel, history, ChartRange.Year);

        Assert.Equal(2, history.RequestedRanges.Count(r => r == ChartRange.Year));
    }

    [Fact]
    public async Task ChartUnavailable_FallsBackToDailyChange_AndExplainsWhy()
    {
        var history = new FakeHistoryService((_, instruments) =>
            Task.FromResult<IReadOnlyList<HistoryResult>>(HistoryUnavailable.ForAll(instruments, "No chart data.")));
        var quotes = new FakeQuoteService((_, instruments) => Task.FromResult<IReadOnlyList<QuoteResult>>(
            [.. instruments.Select(i => new QuoteAvailable(new Quote(i, 99m, 100m, "EUR", DateTimeOffset.UnixEpoch)))]));
        var viewModel = TestViewModels.Create(quotes, new FakeTimeProvider(), history: history, ids: "a");

        await viewModel.RefreshAsync(CancellationToken.None);

        var item = viewModel.Items[0];
        Assert.Empty(item.ChartValues);
        Assert.Equal("No chart data.", item.ChartStatus);
        Assert.Equal("-1.00 %", item.ChangeText);
        Assert.Equal("Daily change (chart unavailable)", item.ChangeDescription);
    }

    private static async Task SelectAndWaitAsync(TickerViewModel viewModel, FakeHistoryService history, ChartRange range)
    {
        var before = history.RequestedRanges.Count;
        viewModel.SelectedRange = Range(range);
        if (range != ChartRange.Day)
        {
            await WaitUntilAsync(() => history.RequestedRanges.Count > before);
        }
        else
        {
            // Day is the default, so selecting it changes nothing: load it the way a refresh would.
            await viewModel.RefreshAsync(CancellationToken.None);
        }
    }

    private static ChartRangeOption Range(ChartRange range) => TickerViewModel.Ranges.Single(r => r.Range == range);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The expected state was not reached within 5 seconds.");
            }

            await Task.Delay(10);
        }
    }
}
