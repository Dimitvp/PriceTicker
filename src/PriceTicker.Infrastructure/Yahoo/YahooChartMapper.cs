using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.Yahoo;

/// <summary>Maps one <c>/v8/finance/chart</c> result to a quote or a price history.</summary>
internal static class YahooChartMapper
{
    public static QuoteResult ToQuote(Instrument instrument, YahooChartResult? chart, DateTimeOffset now)
    {
        var meta = chart?.Meta;
        if (meta?.RegularMarketPrice is not { } price)
        {
            return new QuoteUnavailable(instrument, "No price for symbol.");
        }

        if (price < 0m)
        {
            return new QuoteUnavailable(instrument, "Invalid price.");
        }

        if (meta.RegularMarketTime is not { } unixSeconds || !UnixTime.TryFromSeconds(unixSeconds, out var asOf))
        {
            return new QuoteUnavailable(instrument, "No timestamp for price.");
        }

        var quote = new Quote(
            instrument,
            price,
            meta.ChartPreviousClose ?? meta.PreviousClose,
            meta.Currency ?? string.Empty,
            asOf,
            ToMarket(meta, now));

        return new QuoteAvailable(quote);
    }

    // currentTradingPeriod.regular is today's (or the next) regular session.
    private static Market? ToMarket(YahooChartMeta meta, DateTimeOffset now)
    {
        var exchange = meta.FullExchangeName ?? string.Empty;
        var session = meta.CurrentTradingPeriod?.Regular;

        if (session?.Start is { } startSeconds
            && session.End is { } endSeconds
            && UnixTime.TryFromSeconds(startSeconds, out var start)
            && UnixTime.TryFromSeconds(endSeconds, out var end))
        {
            return Market.ForSession(exchange, start, end, now);
        }

        return exchange.Length == 0 ? null : new Market(exchange, MarketState.Unknown);
    }

    public static HistoryResult ToHistory(Instrument instrument, ChartRange range, YahooChartResult? chart)
    {
        IReadOnlyList<long> timestamps = chart?.Timestamps ?? [];
        IReadOnlyList<decimal?> closes = chart?.Indicators?.Quote is [var series, ..] ? series.Close ?? [] : [];

        // Timestamps and closes are parallel arrays; gaps (null closes) are skipped.
        var points = timestamps
            .Zip(closes)
            .Where(pair => pair.Second is >= 0m)
            .Select(pair => UnixTime.TryFromSeconds(pair.First, out var time)
                ? new PricePoint(time, pair.Second!.Value)
                : null)
            .OfType<PricePoint>()
            .ToList();

        var previousClose = chart?.Meta?.ChartPreviousClose;
        if (range == ChartRange.Day)
        {
            (points, previousClose) = LastSession(points, previousClose);
        }

        return points.Count == 0
            ? new HistoryUnavailable(instrument, "No chart data.")
            : new HistoryAvailable(new PriceHistory(instrument, range, points, previousClose));
    }

    /// <summary>
    /// 1D is the last trading session, measured from the close of the session before it, like the
    /// broker's daily change. Until a new session has two bars (Yahoo's intraday data lags the open)
    /// it stays on the previous one, so the chart is never blank.
    /// </summary>
    private static (List<PricePoint> Points, decimal? PreviousClose) LastSession(List<PricePoint> points, decimal? fallbackPreviousClose)
    {
        // The exchanges used trade within one UTC day (08:00–16:30), so the UTC date names the session.
        var sessions = points.GroupBy(point => point.Time.UtcDateTime.Date).ToList();
        var latest = sessions.Count > 1 && sessions[^1].Count() < 2 ? sessions.Count - 2 : sessions.Count - 1;
        if (latest < 0)
        {
            return (points, fallbackPreviousClose);
        }

        var previousClose = latest > 0 ? sessions[latest - 1].Last().Price : fallbackPreviousClose;
        return ([.. sessions[latest]], previousClose);
    }
}
