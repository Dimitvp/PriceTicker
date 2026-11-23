using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;
using PriceTicker.App.Platform;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.History;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Tests.TestDoubles;

internal static class TestViewModels
{
    /// <summary>A gold bar panel with no bar configured: hidden, and never calls the dealers.</summary>
    public static GoldBarViewModel NoGoldBar() =>
        new(new FakeBullionService([]), Options.Create(new GoldBarOptions()));

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    public static TickerViewModel Create(
        IQuoteService quotes,
        TimeProvider clock,
        IAutoStart? autoStart = null,
        IPriceHistoryService? history = null,
        GoldBarViewModel? goldBar = null,
        params string[] ids)
    {
        var options = new TickerOptions
        {
            RefreshInterval = RefreshInterval,
            Instruments = [.. ids.Select(id => new InstrumentOptions
            {
                Id = id,
                DisplayName = id,
                Symbol = id,
                Provider = "Fake",
            })],
        };

        return new TickerViewModel(
            quotes,
            history ?? FakeHistoryService.Returning(5m, 6m),
            goldBar ?? NoGoldBar(),
            Options.Create(options),
            clock,
            autoStart ?? new FakeAutoStart(),
            NullLogger<TickerViewModel>.Instance);
    }
}
