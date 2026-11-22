using System.Threading.Channels;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Tests.TestDoubles;

internal sealed class FakeQuoteProvider(
    string key,
    Func<IReadOnlyList<Instrument>, IReadOnlyList<QuoteResult>> respond) : IQuoteProvider
{
    public string Key => key;

    public List<IReadOnlyList<Instrument>> Calls { get; } = [];

    public static FakeQuoteProvider PricingEverythingAt(string key, decimal price) =>
        new(key, instruments => [.. instruments.Select(i =>
            new QuoteAvailable(new Quote(i, price, null, "EUR", DateTimeOffset.UnixEpoch)))]);

    public static FakeQuoteProvider Throwing(string key, Exception exception) =>
        new(key, _ => throw exception);

    public Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(instruments);
        return Task.FromResult(respond(instruments));
    }
}

/// <summary>
/// Quote service whose answers the test controls. Every call is signalled, so tests can await
/// "the next call happened" instead of polling.
/// </summary>
internal sealed class FakeQuoteService(
    Func<int, IReadOnlyList<Instrument>, Task<IReadOnlyList<QuoteResult>>> respond) : IQuoteService
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);
    private readonly Channel<int> _calls = Channel.CreateUnbounded<int>();
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public static FakeQuoteService PricingEverythingAt(decimal price) =>
        new((_, instruments) => Task.FromResult(PriceAll(instruments, price)));

    public static IReadOnlyList<QuoteResult> PriceAll(IReadOnlyList<Instrument> instruments, decimal price) =>
        [.. instruments.Select(i => new QuoteAvailable(new Quote(i, price, null, "EUR", DateTimeOffset.UnixEpoch)))];

    public Task WaitForCallAsync() =>
        _calls.Reader.ReadAsync().AsTask().WaitAsync(SignalTimeout);

    public Task<IReadOnlyList<QuoteResult>> GetQuotesAsync(
        IReadOnlyList<Instrument> instruments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var callNumber = Interlocked.Increment(ref _callCount);
        _calls.Writer.TryWrite(callNumber);
        return respond(callNumber, instruments);
    }
}
