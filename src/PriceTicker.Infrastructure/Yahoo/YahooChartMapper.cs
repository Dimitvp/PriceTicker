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

    /// <summary>
    /// Five-minute bars in one trading day of the European exchanges used (09:00–17:30): the 1D
    /// chart is a rolling day of trading, so right after the open it still shows the last session.
    /// </summary>
    internal const int DayBars = 102;

    public static HistoryResult ToHistory(Instrument instrument, ChartRange range, YahooChartResult? chart)
    {
        IReadOnlyList<long> timestamps = chart?.Timestamps ?? [];
        IReadOnlyList<decimal?> closes = chart?.Indicators?.Quote is [var series, ..] ? series.Close ?? [] : [];
        var previousClose = chart?.Meta?.ChartPreviousClose;

        if (range == ChartRange.Day && timestamps.Count > DayBars)
        {
            // Bars are counted as time slots, not prices: an illiquid ETF with empty bars still gets one day.
            var start = timestamps.Count - DayBars;
            previousClose = closes.Take(start).LastOrDefault(close => close is >= 0m) ?? previousClose;
            timestamps = [.. timestamps.Skip(start)];
            closes = [.. closes.Skip(start)];
        }

        // Timestamps and closes are parallel arrays; gaps (null closes) are skipped.
        var points = timestamps
            .Zip(closes)
            .Where(pair => pair.Second is >= 0m)
            .Select(pair => UnixTime.TryFromSeconds(pair.First, out var time)
                ? new PricePoint(time, pair.Second!.Value)
                : null)
            .OfType<PricePoint>()
            .ToList();

        return points.Count == 0
            ? new HistoryUnavailable(instrument, "No chart data.")
            : new HistoryAvailable(new PriceHistory(instrument, range, points, previousClose));
    }
}
