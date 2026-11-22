using PriceTicker.Core.Instruments;

namespace PriceTicker.App.Configuration;

/// <summary>The <c>Ticker</c> section of appsettings.json.</summary>
public sealed class TickerOptions
{
    public const string SectionName = "Ticker";

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    public IList<InstrumentOptions> Instruments { get; init; } = [];
}

/// <summary>One entry of <c>Ticker:Instruments</c>. Validated by <see cref="TickerOptionsValidator"/>.</summary>
public sealed class InstrumentOptions
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public Instrument ToInstrument() => new(Id, DisplayName, Symbol, Provider);
}
