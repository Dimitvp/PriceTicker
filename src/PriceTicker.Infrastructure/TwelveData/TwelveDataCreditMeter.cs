using PriceTicker.Core.Quotas;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>Outcome of asking the meter for credits.</summary>
public enum CreditCheck
{
    Reserved,

    /// <summary>The per-minute allowance is spent; it frees up within a minute.</summary>
    MinuteLimit,

    /// <summary>The daily allowance is spent; it starts again at midnight UTC.</summary>
    DailyLimit,
}

/// <summary>
/// Counts the Twelve Data credits this app spends, so it stays inside the free plan (8 a minute,
/// 800 a day counted from midnight UTC) and can show what is left. The server's own count, read
/// from <c>/api_usage</c>, replaces the local one whenever it arrives: it also includes credits
/// spent elsewhere with the same key. Thread-safe, as providers send requests in parallel.
/// </summary>
public sealed class TwelveDataCreditMeter
{
    public const string ProviderName = "Twelve Data";
    public const int CreditsPerMinute = 8;
    public const int DefaultDailyLimit = 800;

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    // One entry per credit spent within the last minute.
    private readonly Queue<DateTimeOffset> _lastMinute = new();
    private DateOnly _day;
    private int _usedToday;
    private int _dailyLimit = DefaultDailyLimit;
    private DateTimeOffset? _lastSynchronised;

    public TwelveDataCreditMeter(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Takes <paramref name="credits"/> from both allowances, or nothing when either would be exceeded.</summary>
    public CreditCheck TryReserve(int credits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(credits);

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            StartNewDayIfDue(now);
            ForgetOlderThanAMinute(now);

            if (_usedToday + credits > _dailyLimit)
            {
                return CreditCheck.DailyLimit;
            }

            if (_lastMinute.Count + credits > CreditsPerMinute)
            {
                return CreditCheck.MinuteLimit;
            }

            for (var i = 0; i < credits; i++)
            {
                _lastMinute.Enqueue(now);
            }

            _usedToday += credits;
            return CreditCheck.Reserved;
        }
    }

    /// <summary>Adopts the server's count for today.</summary>
    public void Synchronise(int usedToday, int dailyLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usedToday);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dailyLimit);

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            StartNewDayIfDue(now);
            _usedToday = usedToday;
            _dailyLimit = dailyLimit;
            _lastSynchronised = now;
        }
    }

    /// <summary>The server's count has not been read today, or not for <paramref name="maxAge"/>.</summary>
    public bool NeedsSynchronising(TimeSpan maxAge)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            return _lastSynchronised is not { } last
                || DateOnly.FromDateTime(last.UtcDateTime) != DateOnly.FromDateTime(now.UtcDateTime)
                || now - last >= maxAge;
        }
    }

    public DailyQuota Snapshot()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            StartNewDayIfDue(now);
            var nextMidnight = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            return new DailyQuota(ProviderName, _usedToday, _dailyLimit, nextMidnight);
        }
    }

    // Call under the lock.
    private void StartNewDayIfDue(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (today != _day)
        {
            _day = today;
            _usedToday = 0;
        }
    }

    // Call under the lock.
    private void ForgetOlderThanAMinute(DateTimeOffset now)
    {
        while (_lastMinute.TryPeek(out var spentAt) && now - spentAt >= Minute)
        {
            _lastMinute.Dequeue();
        }
    }
}
