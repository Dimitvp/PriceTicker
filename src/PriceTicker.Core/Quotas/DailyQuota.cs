namespace PriceTicker.Core.Quotas;

/// <summary>How much of a provider's daily request allowance has been spent.</summary>
public sealed record DailyQuota
{
    public DailyQuota(string provider, int used, int limit, DateTimeOffset resetsAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentOutOfRangeException.ThrowIfNegative(used);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        Provider = provider;
        Used = used;
        Limit = limit;
        ResetsAt = resetsAt;
    }

    /// <summary>Display name, e.g. "Twelve Data".</summary>
    public string Provider { get; }

    public int Used { get; }

    public int Limit { get; }

    /// <summary>When the allowance starts again from zero.</summary>
    public DateTimeOffset ResetsAt { get; }

    public int Left => Math.Max(0, Limit - Used);
}
