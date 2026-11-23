using Microsoft.Extensions.Logging.Abstractions;
using PriceTicker.Core.Bullion;

namespace PriceTicker.Tests.Core;

public sealed class BullionTests
{
    [Fact]
    public void Targets_AreBuybackPricesForEachProfitPercent()
    {
        var holding = new BullionHolding("Valcambi 10 g", 1000m, [7m, 5m]);

        Assert.Equal(new[] { 5m, 7m }, holding.Targets.Select(t => t.Percent));
        Assert.Equal(new[] { 1050.00m, 1070.00m }, holding.Targets.Select(t => t.BuybackPrice));
    }

    [Theory]
    [InlineData(1004.64, null)]
    [InlineData(1049.99, null)]
    [InlineData(1050, 5)]
    [InlineData(1069.99, 5)]
    [InlineData(1100, 7)]
    public void HighestTargetReachedBy_ComparesTheBuybackPrice(decimal buyback, int? expectedPercent)
    {
        var holding = new BullionHolding("Valcambi 10 g", 1000m, [5m, 7m]);

        Assert.Equal(expectedPercent, (int?)holding.HighestTargetReachedBy(buyback)?.Percent);
    }

    [Fact]
    public async Task Service_KeepsDealerOrder_AndContainsAFailingDealer()
    {
        var service = new BullionService(
            [new StubDealer("Tavex", () => throw new InvalidOperationException("boom")), new StubDealer("igold", () => Quote("igold"))],
            NullLogger<BullionService>.Instance);

        var results = await service.GetQuotesAsync(CancellationToken.None);

        Assert.Equal(new[] { "Tavex", "igold" }, results.Select(r => r.Dealer));
        Assert.Equal("Dealer error.", Assert.IsType<BullionQuoteUnavailable>(results[0]).Reason);
        Assert.IsType<BullionQuoteAvailable>(results[1]);
    }

    private static BullionQuoteAvailable Quote(string dealer) =>
        new BullionQuoteAvailable(new BullionQuote(dealer, 1130.34m, 1104.64m, DateTimeOffset.UnixEpoch));

    private sealed class StubDealer(string name, Func<BullionQuoteResult> respond) : IBullionDealer
    {
        public string Name => name;

        public Task<BullionQuoteResult> GetQuoteAsync(CancellationToken cancellationToken) => Task.FromResult(respond());
    }
}
