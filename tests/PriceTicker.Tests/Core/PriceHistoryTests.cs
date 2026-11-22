using Microsoft.Extensions.Logging.Abstractions;
using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Core;

public sealed class PriceHistoryTests
{
    private static readonly Instrument Instrument = TestInstruments.Create("a");

    [Fact]
    public void Constructor_SortsPointsOldestFirst()
    {
        var history = new PriceHistory(Instrument, ChartRange.Week, [Point(2, 12m), Point(0, 10m), Point(1, 11m)], previousClose: null);

        Assert.Equal(new[] { 10m, 11m, 12m }, history.Points.Select(p => p.Price));
    }

    [Fact]
    public void Baseline_WithPreviousClose_UsesIt()
    {
        var history = new PriceHistory(Instrument, ChartRange.Day, [Point(0, 10m)], previousClose: 8m);

        Assert.Equal(8m, history.Baseline);
        Assert.Equal(25m, history.ChangePercentTo(10m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Baseline_WithoutUsablePreviousClose_IsFirstPoint(int? previousClose)
    {
        var history = new PriceHistory(Instrument, ChartRange.Year, [Point(1, 12m), Point(0, 10m)], previousClose);

        Assert.Equal(10m, history.Baseline);
        Assert.Equal(20m, history.ChangePercentTo(12m));
    }

    [Fact]
    public void Constructor_WithoutPoints_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PriceHistory(Instrument, ChartRange.Day, [], null));
    }

    [Fact]
    public async Task PriceHistoryService_RoutesByProviderKey_AndReportsUnknownProviders()
    {
        var provider = new StubHistoryProvider("Alpha");
        var service = new PriceHistoryService([provider], NullLogger<PriceHistoryService>.Instance);

        var results = await service.GetHistoryAsync(
            [TestInstruments.Create("a", "Alpha"), TestInstruments.Create("b", "Missing")],
            ChartRange.ThreeMonths,
            CancellationToken.None);

        Assert.IsType<HistoryAvailable>(results[0]);
        Assert.Contains("Missing", Assert.IsType<HistoryUnavailable>(results[1]).Reason, StringComparison.Ordinal);
        Assert.Equal(ChartRange.ThreeMonths, Assert.Single(provider.Ranges));
    }

    private static PricePoint Point(int hour, decimal price) => new(DateTimeOffset.UnixEpoch.AddHours(hour), price);

    private sealed class StubHistoryProvider(string key) : IPriceHistoryProvider
    {
        public string Key => key;

        public List<ChartRange> Ranges { get; } = [];

        public Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
            IReadOnlyList<Instrument> instruments,
            ChartRange range,
            CancellationToken cancellationToken)
        {
            Ranges.Add(range);
            return Task.FromResult(FakeHistoryService.History(range, instruments, 1m, 2m));
        }
    }
}
