using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceTicker.Infrastructure.Bullion;

/// <summary>Buy and buyback prices read from a dealer's product page, from the customer's side.</summary>
internal readonly record struct DealerPrices(decimal BuyPrice, decimal BuybackPrice);

/// <summary>
/// Tavex product pages embed the price list as JSON in a <c>data-pricelist</c> attribute on the main
/// product's price (<c>js-product-price-from</c>), e.g.
/// <c>{"buy":[{"price":1103,"quantityFrom":1,...}],"sell":[{"price":1133,"quantityFrom":1,...}]}</c>.
/// "sell" is what Tavex sells for (you buy), "buy" is what Tavex pays (you sell); the tier for a
/// single piece is the one starting at quantity 1.
/// </summary>
internal static partial class TavexPageParser
{
    public static DealerPrices? Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var match = PriceListAttribute().Match(html);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups["json"].Value));
            return FirstTierPrice(document.RootElement, "sell") is { } buy
                && FirstTierPrice(document.RootElement, "buy") is { } buyback
                    ? new DealerPrices(buy, buyback)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? FirstTierPrice(JsonElement root, string side)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(side, out var tiers)
            || tiers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var objects = tiers.EnumerateArray().Where(tier => tier.ValueKind == JsonValueKind.Object).ToList();
        var single = objects.FirstOrDefault(IsSinglePieceTier);
        var tier = single.ValueKind == JsonValueKind.Object ? single : objects.FirstOrDefault();

        return tier.ValueKind == JsonValueKind.Object
            && tier.TryGetProperty("price", out var price)
            && price.ValueKind == JsonValueKind.Number
            && price.TryGetDecimal(out var value)
            && value > 0m
                ? value
                : null;
    }

    private static bool IsSinglePieceTier(JsonElement tier) =>
        tier.TryGetProperty("quantityFrom", out var from)
        && from.ValueKind == JsonValueKind.Number
        && from.TryGetInt32(out var quantity)
        && quantity == 1;

    // Anchored to the main product's price element so a related product's list is never picked.
    [GeneratedRegex("""js-product-price-from[^>]{0,300}?data-pricelist='(?<json>[^']{1,4000})'""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PriceListAttribute();
}

/// <summary>
/// igold product pages show the main product's prices in spans with the classes
/// <c>productUpdatePriceSell</c> (you buy) and <c>productUpdatePriceBuy</c> (you sell), e.g.
/// <c>&lt;span class="productUpdatePriceSell"&gt;1130.34 €&lt;/span&gt;</c>. Related products use
/// suffixed classes, which the exact class match skips.
/// </summary>
internal static partial class IgoldPageParser
{
    public static DealerPrices? Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return PriceIn(SellSpan().Match(html)) is { } buy && PriceIn(BuySpan().Match(html)) is { } buyback
            ? new DealerPrices(buy, buyback)
            : null;
    }

    private static decimal? PriceIn(Match match) =>
        match.Success && PriceText.TryParse(match.Groups["price"].Value, out var price) ? price : null;

    [GeneratedRegex("""class="productUpdatePriceSell">(?<price>[^<]{1,40})<""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SellSpan();

    [GeneratedRegex("""class="productUpdatePriceBuy">(?<price>[^<]{1,40})<""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BuySpan();
}

/// <summary>
/// Reads a displayed price such as "1130.34 €", "1 130,34 €" or "1,130.34". Only the first number
/// is read, so text showing two amounts (e.g. "1130.34 € / 2210.70 лв.") cannot merge them.
/// </summary>
internal static partial class PriceText
{
    public static bool TryParse(string text, out decimal price)
    {
        price = 0m;
        var number = FirstNumber().Match(WebUtility.HtmlDecode(text));
        if (!number.Success)
        {
            return false;
        }

        var digits = new string([.. number.Value.Where(c => char.IsAsciiDigit(c) || c is '.' or ',')]);

        // The last separator is the decimal one when two digits follow it; any other is grouping.
        var lastSeparator = digits.LastIndexOfAny(['.', ',']);
        var hasDecimals = lastSeparator >= 0 && digits.Length - lastSeparator - 1 is 1 or 2;
        var integerPart = (hasDecimals ? digits[..lastSeparator] : digits).Replace(".", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        var normalized = hasDecimals ? $"{integerPart}.{digits[(lastSeparator + 1)..]}" : integerPart;

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price)
            && price > 0m;
    }

    // Digits with grouping spaces (incl. no-break), dots or commas inside; nothing after a gap of letters or symbols.
    [GeneratedRegex(@"\d(?:[\d.,]|[ \u00A0\u202F](?=\d))*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FirstNumber();
}
