using System.Text.Json.Serialization;

namespace PriceTicker.Infrastructure.Yahoo;

// Only the fields the ticker reads from /v8/finance/chart are mapped.

internal sealed record YahooChartResponse(
    [property: JsonPropertyName("chart")] YahooChart? Chart);

internal sealed record YahooChart(
    [property: JsonPropertyName("result")] IReadOnlyList<YahooChartResult>? Result);

internal sealed record YahooChartResult(
    [property: JsonPropertyName("meta")] YahooChartMeta? Meta,
    [property: JsonPropertyName("timestamp")] IReadOnlyList<long>? Timestamps,
    [property: JsonPropertyName("indicators")] YahooIndicators? Indicators);

internal sealed record YahooChartMeta(
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("regularMarketPrice")] decimal? RegularMarketPrice,
    [property: JsonPropertyName("chartPreviousClose")] decimal? ChartPreviousClose,
    [property: JsonPropertyName("previousClose")] decimal? PreviousClose,
    [property: JsonPropertyName("regularMarketTime")] long? RegularMarketTime,
    [property: JsonPropertyName("fullExchangeName")] string? FullExchangeName,
    [property: JsonPropertyName("currentTradingPeriod")] YahooTradingPeriods? CurrentTradingPeriod);

internal sealed record YahooTradingPeriods(
    [property: JsonPropertyName("regular")] YahooTradingPeriod? Regular);

// Unix seconds.
internal sealed record YahooTradingPeriod(
    [property: JsonPropertyName("start")] long? Start,
    [property: JsonPropertyName("end")] long? End);

internal sealed record YahooIndicators(
    [property: JsonPropertyName("quote")] IReadOnlyList<YahooQuoteSeries>? Quote);

// Entries are null for intervals without trades.
internal sealed record YahooQuoteSeries(
    [property: JsonPropertyName("close")] IReadOnlyList<decimal?>? Close);

[JsonSerializable(typeof(YahooChartResponse))]
internal sealed partial class YahooJsonContext : JsonSerializerContext;
