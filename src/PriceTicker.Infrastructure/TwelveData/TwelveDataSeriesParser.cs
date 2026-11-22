using System.Globalization;
using System.Text.Json;
using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using static PriceTicker.Infrastructure.TwelveData.TwelveDataJson;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Turns a single-symbol Twelve Data <c>/time_series</c> payload into a price history.
/// Values arrive newest first with "datetime" in UTC (the request asks for it) and prices as strings.
/// </summary>
internal static class TwelveDataSeriesParser
{
    private static readonly string[] DateTimeFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];

    public static HistoryResult Parse(JsonElement root, Instrument instrument, ChartRange range)
    {
        if (IsError(root, out var error))
        {
            return new HistoryUnavailable(instrument, error);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return new HistoryUnavailable(instrument, "Unexpected response.");
        }

        var points = values.EnumerateArray()
            .Select(ToPoint)
            .OfType<PricePoint>()
            .ToList();

        // No previous close here: the oldest point is the baseline.
        return points.Count == 0
            ? new HistoryUnavailable(instrument, "No chart data.")
            : new HistoryAvailable(new PriceHistory(instrument, range, points, previousClose: null));
    }

    private static PricePoint? ToPoint(JsonElement value) =>
        TryGetString(value, "datetime") is { } text
        && DateTime.TryParseExact(
            text,
            DateTimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var time)
        && TryGetDecimal(value, "close", out var price)
        && price >= 0m
            ? new PricePoint(new DateTimeOffset(time, TimeSpan.Zero), price)
            : null;
}
