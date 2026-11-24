using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.App.Platform;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class TickerViewModelTests
{
    private static readonly TimeSpan Interval = TestViewModels.RefreshInterval;

    public TickerViewModelTests()
    {
        // CurrentCulture flows with the async context, so this affects only this test.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Constructor_CreatesOneRowPerConfiguredInstrument()
    {
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), "a", "b");

        Assert.Equal(new[] { "a", "b" }, viewModel.Items.Select(item => item.InstrumentId));
        Assert.Equal("Loading…", viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task RefreshAsync_AppliesResultsAndRecordsTime()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 14, 30, 0, TimeSpan.Zero));
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), clock, "a");

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("7.00 EUR", viewModel.Items[0].PriceText);
        Assert.Equal(clock.GetLocalNow(), viewModel.LastUpdated);
    }

    [Fact]
    public async Task RefreshAsync_WhenNothingArrives_KeepsPreviousUpdateTime()
    {
        var service = new FakeQuoteService((_, instruments) =>
            Task.FromResult<IReadOnlyList<QuoteResult>>(QuoteUnavailable.ForAll(instruments, "Network error.")));
        var viewModel = CreateViewModel(service, new FakeTimeProvider(), "a");

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Null(viewModel.LastUpdated);
        Assert.Equal("Loading…", viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task RefreshAsync_WhileAnotherRefreshRuns_IsIgnored()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<QuoteResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeQuoteService((_, _) => gate.Task);
        var viewModel = CreateViewModel(service, new FakeTimeProvider(), "a");

        var first = viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.RefreshAsync(CancellationToken.None);
        gate.SetResult([]);
        await first;

        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task RunAutoRefreshAsync_RefreshesAtStartAndOnEveryTick()
    {
        var clock = new FakeTimeProvider();
        var service = FakeQuoteService.PricingEverythingAt(7m);
        var viewModel = CreateViewModel(service, clock, "a");
        using var cancellation = new CancellationTokenSource();

        var loop = viewModel.RunAutoRefreshAsync(cancellation.Token);
        await service.WaitForCallAsync();

        clock.Advance(Interval);
        await service.WaitForCallAsync();

        clock.Advance(Interval);
        await service.WaitForCallAsync();

        await cancellation.CancelAsync();
        await loop;
        Assert.Equal(3, service.CallCount);
    }

    [Fact]
    public async Task RunAutoRefreshAsync_AfterAFailedRound_KeepsRefreshing()
    {
        var clock = new FakeTimeProvider();
        var service = new FakeQuoteService((call, instruments) => call == 1
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(FakeQuoteService.PriceAll(instruments, 7m)));
        var viewModel = CreateViewModel(service, clock, "a");
        using var cancellation = new CancellationTokenSource();

        var loop = viewModel.RunAutoRefreshAsync(cancellation.Token);
        await service.WaitForCallAsync();
        // The fake signals as the call starts; the failure reaches the row a moment later.
        await WaitUntilAsync(() => viewModel.Items[0].Detail == "Refresh failed.");

        clock.Advance(Interval);
        await service.WaitForCallAsync();

        await cancellation.CancelAsync();
        await loop;
        Assert.Equal("7.00 EUR", viewModel.Items[0].PriceText);
    }

    [Fact]
    public void StartsWithWindows_ReflectsCurrentSystemState()
    {
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), new FakeAutoStart(isEnabled: true), "a");

        Assert.True(viewModel.StartsWithWindows);
        Assert.True(viewModel.IsAutoStartSupported);
    }

    [Fact]
    public void StartsWithWindows_WhenToggled_UpdatesSystem()
    {
        var autoStart = new FakeAutoStart();
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), autoStart, "a");

        viewModel.StartsWithWindows = true;

        Assert.True(autoStart.IsEnabled);
        Assert.Equal(1, autoStart.ChangeAttempts);
    }

    [Fact]
    public void StartsWithWindows_WhenSystemRefuses_RevertsCheckbox()
    {
        var autoStart = new FakeAutoStart(refuseChanges: true);
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), autoStart, "a");

        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        viewModel.StartsWithWindows = true;

        Assert.False(viewModel.StartsWithWindows);
        Assert.Equal(1, autoStart.ChangeAttempts);
        Assert.Contains(nameof(TickerViewModel.StartsWithWindows), notifications);
    }

    [Fact]
    public void ToggleCommands_FlipTheirSettings()
    {
        var autoStart = new FakeAutoStart();
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), autoStart, "a");

        viewModel.ToggleAlwaysOnTopCommand.Execute(null);
        viewModel.ToggleStartsWithWindowsCommand.Execute(null);

        Assert.False(viewModel.IsAlwaysOnTop);
        Assert.True(viewModel.StartsWithWindows);
        Assert.True(autoStart.IsEnabled);
    }

    [Fact]
    public void StartsWithWindows_WhenUntickRefused_StaysTicked()
    {
        var autoStart = new FakeAutoStart(isEnabled: true, refuseChanges: true);
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), autoStart, "a");

        viewModel.StartsWithWindows = false;

        Assert.True(viewModel.StartsWithWindows);
    }

    [Fact]
    public void StartsWithWindows_UntickWhenNotRegistered_StillClearsSystemEntry()
    {
        // e.g. an entry left by an older copy of the exe: IsEnabled is false, but unticking must still clean up.
        var autoStart = new FakeAutoStart(isEnabled: false);
        var viewModel = CreateViewModel(FakeQuoteService.PricingEverythingAt(7m), new FakeTimeProvider(), autoStart, "a");
        viewModel.StartsWithWindows = true;

        viewModel.StartsWithWindows = false;

        Assert.Equal(2, autoStart.ChangeAttempts);
        Assert.False(autoStart.IsEnabled);
    }

    private static TickerViewModel CreateViewModel(IQuoteService service, TimeProvider clock, params string[] ids) =>
        TestViewModels.Create(service, clock, ids: ids);

    private static TickerViewModel CreateViewModel(IQuoteService service, TimeProvider clock, IAutoStart autoStart, params string[] ids) =>
        TestViewModels.Create(service, clock, autoStart, ids: ids);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }
}
