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
        var timestamps = chart?.Timestamps ?? [];
        var closes = chart?.Indicators?.Quote is [var series, ..] ? series.Close ?? [] : [];

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
            : new HistoryAvailable(new PriceHistory(instrument, range, points, chart?.Meta?.ChartPreviousClose));
    }
}
