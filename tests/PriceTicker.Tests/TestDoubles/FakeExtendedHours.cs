using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotas;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Tests.TestDoubles;

/// <summary>Extended-hours venue whose answers the test controls; by default it trades nothing.</summary>
internal sealed class FakeExtendedHoursService(Func<IReadOnlyList<Instrument>, IReadOnlyList<QuoteResult>> respond)
    : IExtendedHoursQuoteService
{
    public List<IReadOnlyList<Instrument>> Calls { get; } = [];

    public static FakeExtendedHoursService TradingNothing() =>
        new(instruments => QuoteUnavailable.ForAll(instruments, "Not traded."));

    public Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(IReadOnlyList<Instrument> instruments, CancellationToken cancellationToken)
    {
        Calls.Add(instruments);
        return Task.FromResult(respond(instruments));
    }
}

internal sealed class FakeQuotaSource(DailyQuota? quota) : IDailyQuotaSource
{
    public static FakeQuotaSource None() => new(null);

    public Task<DailyQuota?> GetDailyQuotaAsync(CancellationToken cancellationToken) => Task.FromResult(quota);
}
