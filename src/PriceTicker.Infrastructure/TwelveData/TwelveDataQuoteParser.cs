using System.Globalization;
using System.Text.Json;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Turns a Twelve Data <c>/quote</c> payload into results. For a single symbol the API returns
/// the quote object itself; for several it returns an object keyed by symbol. Each quote can
/// instead be an error object (<c>"status": "error"</c>). Numbers arrive as strings.
/// </summary>
internal static class TwelveDataQuoteParser
{
    public static IReadOnlyList<QuoteResult> Parse(JsonElement root, IReadOnlyList<Instrument> instruments)
    {
        if (IsError(root, out var topLevelError))
        {
            return QuoteUnavailable.ForAll(instruments, topLevelError);
        }

        if (instruments.Count == 1)
        {
            return [ParseQuote(root, instruments[0])];
        }

        return [.. instruments.Select(instrument =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(instrument.Symbol, out var element)
                ? ParseQuote(element, instrument)
                : new QuoteUnavailable(instrument, "Symbol missing from response."))];
    }

    private static QuoteResult ParseQuote(JsonElement element, Instrument instrument)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new QuoteUnavailable(instrument, "Unexpected response.");
        }

        if (IsError(element, out var error))
        {
            return new QuoteUnavailable(instrument, error);
        }

        if (!TryGetDecimal(element, "close", out var price))
        {
            return new QuoteUnavailable(instrument, "No price for symbol.");
        }

        if (price < 0m)
        {
            return new QuoteUnavailable(instrument, "Invalid price.");
        }

        if (!TryGetInt64(element, "timestamp", out var unixSeconds)
            || !UnixTime.TryFromSeconds(unixSeconds, out var asOf))
        {
            return new QuoteUnavailable(instrument, "No timestamp for price.");
        }

        decimal? previousClose = TryGetDecimal(element, "previous_close", out var previous) ? previous : null;

        var quote = new Quote(
            instrument,
            price,
            previousClose,
            GetCurrency(element),
            asOf);

        return new QuoteAvailable(quote);
    }

    private static bool IsError(JsonElement element, out string message)
    {
        message = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() != "error")
        {
            return false;
        }

        // The API's own message is not shown: it can echo request details. The code is enough.
        message = element.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.Number
            && code.TryGetInt32(out var number)
            ? $"Provider error {number}."
            : "Provider error.";
        return true;
    }

    // Forex and crypto quotes report the quote currency; stocks and ETFs report "currency".
    private static string GetCurrency(JsonElement element) =>
        TryGetString(element, "currency_quote") ?? TryGetString(element, "currency") ?? string.Empty;

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        return TryGetString(element, propertyName) is { } text
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }
}
