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

    /// <summary>
    /// Optional headed sections of the widget, in display order. Instruments in no group are shown
    /// last, without a heading. Personal (it describes the strategy): keep it in appsettings.Local.json.
    /// </summary>
    public IList<GroupOptions> Groups { get; init; } = [];
}

/// <summary>One entry of <c>Ticker:Groups</c>: a heading over some instruments.</summary>
public sealed class GroupOptions
{
    public string Title { get; set; } = string.Empty;

    /// <summary>A reminder of what the group is for, shown after the title.</summary>
    public string? Note { get; set; }

    /// <summary>Instrument <see cref="InstrumentOptions.Id"/>s, in display order.</summary>
    public IList<string> Instruments { get; init; } = [];
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
/// <c>Ticker:Holdings:&lt;id&gt;</c>. Give the purchase price for a one-off buy (the base of the broker's
/// alerts), and/or Quantity with Invested for the position as the broker shows it.
/// </summary>
public sealed class HoldingOptions
{
    /// <summary>Price of a one-off purchase, excluding commissions. Leave out for regular (DCA) purchases.</summary>
    public decimal? PurchasePrice { get; set; }

    /// <summary>The "review the thesis" alert (−20%).</summary>
    public decimal? ReviewAtOrBelow { get; set; }

    /// <summary>The exit alert (−40%).</summary>
    public decimal? SellAtOrBelow { get; set; }

    /// <summary>The take-profit alert (+50%, sell half).</summary>
    public decimal? SellAtOrAbove { get; set; }

    /// <summary>Units held (IBKR: Position). Set together with <see cref="Invested"/>.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Total money put in, commissions included (IBKR: Position × Avg Price, or Cost Basis).</summary>
    public decimal? Invested { get; set; }

    public bool HasPosition => Quantity is not null && Invested is not null;

    public Holding ToHolding() =>
        new(
            PurchasePrice,
            new PriceAlerts(ReviewAtOrBelow, SellAtOrBelow, SellAtOrAbove),
            HasPosition ? new Position(Quantity!.Value, Invested!.Value) : null);
}

