namespace PriceTicker.Core.Quotas;

/// <summary>A provider whose free plan allows only so many requests a day.</summary>
public interface IDailyQuotaSource
{
    /// <summary>
    /// The allowance as known now, or <c>null</c> when the provider is not in use (no API key).
    /// May ask the provider for its own count, which can itself cost a request.
    /// </summary>
    Task<DailyQuota?> GetDailyQuotaAsync(CancellationToken cancellationToken);
}
