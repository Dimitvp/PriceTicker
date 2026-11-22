using System.Text.Json;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;
using static PriceTicker.Infrastructure.TwelveData.TwelveDataJson;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Turns a Twelve Data <c>/quote</c> payload into results. For a single symbol the API returns
/// the quote object itself; for several it returns an object keyed by symbol.
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
            GetCurrency(element, instrument.Symbol),
            asOf);

        return new QuoteAvailable(quote);
    }

    // Stocks and ETFs report "currency"; crypto pairs may report nothing, so for a pair such as
    // "BTC/EUR" the quote currency is taken from the symbol itself.
    private static string GetCurrency(JsonElement element, string symbol) =>
        TryGetString(element, "currency_quote")
        ?? TryGetString(element, "currency")
        ?? QuoteCurrencyOf(symbol)
        ?? string.Empty;

    private static string? QuoteCurrencyOf(string symbol)
    {
        var separator = symbol.LastIndexOf('/');
        var candidate = separator >= 0 ? symbol[(separator + 1)..] : string.Empty;
        return candidate.Length == 3 && candidate.All(char.IsAsciiLetterUpper) ? candidate : null;
    }
}
