using PriceTicker.Core.Bullion;

namespace PriceTicker.Tests.TestDoubles;

internal sealed class FakeBullionService(IReadOnlyList<BullionQuoteResult> results) : IBullionService
{
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<BullionQuoteResult>> GetQuotesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(results);
    }

    public static BullionQuoteResult Quote(string dealer, decimal buy, decimal buyback) =>
        new BullionQuoteAvailable(new BullionQuote(dealer, buy, buyback, DateTimeOffset.UnixEpoch));
}
