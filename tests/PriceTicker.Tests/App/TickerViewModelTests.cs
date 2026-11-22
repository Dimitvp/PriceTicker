using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.App.Configuration;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class TickerViewModelTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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
        Assert.Equal("Refresh failed.", viewModel.Items[0].Detail);

        clock.Advance(Interval);
        await service.WaitForCallAsync();

        await cancellation.CancelAsync();
        await loop;
        Assert.Equal("7.00 EUR", viewModel.Items[0].PriceText);
    }

    private static TickerViewModel CreateViewModel(IQuoteService service, TimeProvider clock, params string[] ids)
    {
        var options = new TickerOptions
        {
            RefreshInterval = Interval,
            Instruments = [.. ids.Select(id => new InstrumentOptions
            {
                Id = id,
                DisplayName = id,
                Symbol = id,
                Provider = "Fake",
            })],
        };

        return new TickerViewModel(service, Options.Create(options), clock, NullLogger<TickerViewModel>.Instance);
    }
}
