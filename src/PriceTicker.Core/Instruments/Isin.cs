using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PriceTicker.Core.Instruments;

/// <summary>
/// An International Securities Identification Number, e.g. <c>IE00B5BMR087</c>: a two-letter country
/// code, nine letters or digits, and a check digit (ISO 6166), which catches a mistyped character.
/// </summary>
public sealed record Isin
{
    private const int Length = 12;

    private Isin(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? text, [NotNullWhen(true)] out Isin? isin)
    {
        var candidate = text?.Trim().ToUpperInvariant();
        isin = candidate is not null && HasValidShape(candidate) && candidate[^1] - '0' == CheckDigit(candidate[..^1])
            ? new Isin(candidate)
            : null;
        return isin is not null;
    }

    public override string ToString() => Value;

    private static bool HasValidShape(string candidate) =>
        candidate.Length == Length
        && char.IsAsciiLetterUpper(candidate[0])
        && char.IsAsciiLetterUpper(candidate[1])
        && candidate[2..^1].All(char.IsAsciiLetterOrDigit)
        && char.IsAsciiDigit(candidate[^1]);

    // Letters become two digits (A = 10 … Z = 35); then the Luhn algorithm over the digit string.
    private static int CheckDigit(string body)
    {
        var digits = string.Concat(body.Select(c => (char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10)
            .ToString(CultureInfo.InvariantCulture)));

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 0)
            {
                digit *= 2;
                digit = digit > 9 ? digit - 9 : digit;
            }

            sum += digit;
        }

        return (10 - (sum % 10)) % 10;
    }
}
