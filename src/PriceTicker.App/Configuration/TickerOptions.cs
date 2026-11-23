using PriceTicker.Core.Holdings;
using PriceTicker.Core.Instruments;

namespace PriceTicker.App.Configuration;

/// <summary>The <c>Ticker</c> section of appsettings.json.</summary>
public sealed class TickerOptions
{
    public const string SectionName = "Ticker";

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    public IList<InstrumentOptions> Instruments { get; init; } = [];

    /// <summary>
    /// What was paid and the broker alerts, keyed by instrument <see cref="InstrumentOptions.Id"/>.
    /// Personal data: keep it in appsettings.Local.json (git-ignored), not in appsettings.json.
    /// </summary>
    public IDictionary<string, HoldingOptions> Holdings { get; init; } =
        new Dictionary<string, HoldingOptions>(StringComparer.OrdinalIgnoreCase);
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

/// <summary>
/// <c>Ticker:Holdings:&lt;id&gt;</c>. Copy the alert prices from the broker (IBKR price alerts);
/// the purchase price excludes commissions, as the alerts do.
/// </summary>
public sealed class HoldingOptions
{
    public decimal PurchasePrice { get; set; }

    /// <summary>The "review the thesis" alert (−20%).</summary>
    public decimal? ReviewAtOrBelow { get; set; }

    /// <summary>The exit alert (−40%).</summary>
    public decimal? SellAtOrBelow { get; set; }

    /// <summary>The take-profit alert (+50%, sell half).</summary>
    public decimal? SellAtOrAbove { get; set; }

    public Holding ToHolding() =>
        new(PurchasePrice, new PriceAlerts(ReviewAtOrBelow, SellAtOrBelow, SellAtOrAbove));
}
