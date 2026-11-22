using System.Globalization;
using System.Text.Json;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Tolerant readers for Twelve Data payloads: numbers arrive as strings, and any object may be an
/// error object (<c>"status": "error"</c>). Nothing here throws on unexpected shapes.
/// </summary>
internal static class TwelveDataJson
{
    public static bool IsError(JsonElement element, out string message)
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

    public static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        return TryGetString(element, propertyName) is { } text
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
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
