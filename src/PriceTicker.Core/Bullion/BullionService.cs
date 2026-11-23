using Microsoft.Extensions.Logging;

namespace PriceTicker.Core.Bullion;

/// <summary>Reads every registered dealer in parallel; one failing dealer never hides the others.</summary>
public sealed partial class BullionService : IBullionService
{
    private readonly IReadOnlyList<IBullionDealer> _dealers;
    private readonly ILogger<BullionService> _logger;

    public BullionService(IEnumerable<IBullionDealer> dealers, ILogger<BullionService> logger)
    {
        ArgumentNullException.ThrowIfNull(dealers);
        ArgumentNullException.ThrowIfNull(logger);

        _dealers = [.. dealers];
        _logger = logger;
    }

    public async Task<IReadOnlyList<BullionQuoteResult>> GetQuotesAsync(CancellationToken cancellationToken) =>
        await Task.WhenAll(_dealers.Select(dealer => GetSafelyAsync(dealer, cancellationToken))).ConfigureAwait(false);

    private async Task<BullionQuoteResult> GetSafelyAsync(IBullionDealer dealer, CancellationToken cancellationToken)
    {
        try
        {
            return await dealer.GetQuoteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Last line of defence: dealers should not throw.
            LogDealerFailed(ex, dealer.Name);
            return new BullionQuoteUnavailable(dealer.Name, "Dealer error.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Bullion dealer '{Dealer}' failed unexpectedly.")]
    private partial void LogDealerFailed(Exception exception, string dealer);
}
