namespace PriceTicker.Core.Bullion;

/// <summary>A dealer whose current buy and buyback prices for one product can be read.</summary>
/// <remarks>Reports problems as <see cref="BullionQuoteUnavailable"/>; throws only on cancellation.</remarks>
public interface IBullionDealer
{
    string Name { get; }

    Task<BullionQuoteResult> GetQuoteAsync(CancellationToken cancellationToken);
}

/// <summary>Reads every dealer's prices in parallel, in a stable order.</summary>
public interface IBullionService
{
    Task<IReadOnlyList<BullionQuoteResult>> GetQuotesAsync(CancellationToken cancellationToken);
}
