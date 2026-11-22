using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;

namespace PriceTicker.Tests.TestDoubles;

/// <summary>History service whose answers the test controls; records every requested range.</summary>
internal sealed class FakeHistoryService : IPriceHistoryService
{
    private readonly Func<ChartRange, IReadOnlyList<Instrument>, CancellationToken, Task<IReadOnlyList<HistoryResult>>> _respond;
    private readonly List<ChartRange> _requestedRanges = [];

    public FakeHistoryService(Func<ChartRange, IReadOnlyList<Instrument>, Task<IReadOnlyList<HistoryResult>>> respond)
        : this((range, instruments, _) => respond(range, instruments))
    {
    }

    /// <summary>For tests that need the token, e.g. to observe that a load was superseded.</summary>
    public FakeHistoryService(Func<ChartRange, IReadOnlyList<Instrument>, CancellationToken, Task<IReadOnlyList<HistoryResult>>> respond)
    {
        _respond = respond;
    }

    public IReadOnlyList<ChartRange> RequestedRanges
    {
        get
        {
            lock (_requestedRanges)
            {
                return [.. _requestedRanges];
            }
        }
    }

    /// <summary>Every instrument gets the given prices, oldest first, with no previous close.</summary>
    public static FakeHistoryService Returning(params decimal[] prices) =>
        new((range, instruments) => Task.FromResult(History(range, instruments, prices)));

    public static IReadOnlyList<HistoryResult> History(ChartRange range, IReadOnlyList<Instrument> instruments, params decimal[] prices) =>
        [.. instruments.Select(instrument => new HistoryAvailable(new PriceHistory(
            instrument,
            range,
            prices.Select((price, index) => new PricePoint(DateTimeOffset.UnixEpoch.AddHours(index), price)),
            previousClose: null)))];

    public Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
        IReadOnlyList<Instrument> instruments,
        ChartRange range,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_requestedRanges)
        {
            _requestedRanges.Add(range);
        }

        return _respond(range, instruments, cancellationToken);
    }
}
