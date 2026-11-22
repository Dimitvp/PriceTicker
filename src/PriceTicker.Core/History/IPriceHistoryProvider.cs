using PriceTicker.Core.Instruments;

namespace PriceTicker.Core.History;

/// <summary>A source of historical prices.</summary>
/// <remarks>
/// Implementations return exactly one <see cref="HistoryResult"/> per requested instrument,
/// report data problems as <see cref="HistoryUnavailable"/>, and throw only on cancellation.
/// </remarks>
public interface IPriceHistoryProvider : IKeyedProvider
{
    Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
        IReadOnlyList<Instrument> instruments,
        ChartRange range,
        CancellationToken cancellationToken);
}

/// <summary>Fetches price histories for a mixed list of instruments, whatever provider each one uses.</summary>
public interface IPriceHistoryService
{
    /// <summary>One result per instrument, in the order given. Throws only on cancellation.</summary>
    Task<IReadOnlyList<HistoryResult>> GetHistoryAsync(
        IReadOnlyList<Instrument> instruments,
        ChartRange range,
        CancellationToken cancellationToken);
}
