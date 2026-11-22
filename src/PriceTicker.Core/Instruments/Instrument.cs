namespace PriceTicker.Core.Instruments;

/// <summary>
/// Something whose price the ticker shows: an ETF, a metal or a coin, together with
/// the provider that prices it and the symbol that provider knows it by.
/// </summary>
public sealed record Instrument
{
    public Instrument(string id, string displayName, string symbol, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        Id = id;
        DisplayName = displayName;
        Symbol = symbol;
        ProviderKey = providerKey;
    }

    /// <summary>Stable identifier, unique within the configured list.</summary>
    public string Id { get; }

    /// <summary>Short label shown in the widget.</summary>
    public string DisplayName { get; }

    /// <summary>Symbol in the provider's own notation, e.g. <c>SXR8.DE</c> or <c>XAU/EUR</c>.</summary>
    public string Symbol { get; }

    /// <summary>Key of the provider (<see cref="IKeyedProvider"/>) that supplies this instrument's prices and chart.</summary>
    public string ProviderKey { get; }
}
