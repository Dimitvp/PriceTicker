using System.Globalization;
using System.Text.Json;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.Tradegate;

/// <summary>
/// Turns Tradegate's <c>refresh.php</c> payload into a quote, e.g.
/// <c>{ "bid": 72.01, "ask": 72.22, "last": 72.04, "low": "71,50", "close": 70.87, ... }</c>.
/// Numbers mostly arrive as JSON numbers but sometimes as German-formatted strings, so both are read.
/// <c>close</c> is Tradegate's own previous close (at 22:00 CET), not the home exchange's.
/// </summary>
internal static class TradegateQuoteParser
{
    public const string Currency = "EUR";
    public const string VenueName = "Tradegate";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static QuoteResult Parse(JsonElement root, Instrument instrument, DateTimeOffset fetchedAt)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new QuoteUnavailable(instrument, "Not traded on Tradegate.");
        }

        var price = Positive(root, "last") ?? MidPoint(root);
        if (price is not { } last)
        {
            return new QuoteUnavailable(instrument, "No Tradegate price.");
        }

        // Tradegate gives no timestamp: the price is as of the request.
        var quote = new Quote(
            instrument,
            last,
            Positive(root, "close"),
            Currency,
            fetchedAt,
            new Market(VenueName, MarketState.Unknown));

        return new QuoteAvailable(quote);
    }

    private static decimal? MidPoint(JsonElement root) =>
        Positive(root, "bid") is { } bid && Positive(root, "ask") is { } ask ? (bid + ask) / 2m : null;

    private static decimal? Positive(JsonElement root, string name) =>
        TryRead(root, name, out var value) && value > 0m ? value : null;

    internal static bool TryRead(JsonElement root, string name, out decimal value)
    {
        value = 0m;
        if (!root.TryGetProperty(name, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => TryParseText(property.GetString(), out value),
            _ => false,
        };
    }

    // "71,50" or "1.234,56" is German; a string without a comma ("709.38") is read as invariant,
    // so a decimal point is never mistaken for a thousands separator.
    private static bool TryParseText(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var culture = text.Contains(',', StringComparison.Ordinal) ? German : CultureInfo.InvariantCulture;
        return decimal.TryParse(text, NumberStyles.Number, culture, out value);
    }
}
