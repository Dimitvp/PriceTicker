namespace PriceTicker.App.Configuration;

/// <summary>
/// The optional <c>GoldBar</c> section: a physical bar bought from a dealer, priced by the
/// dealers' buy and buyback prices. Without it the gold bar panel is hidden.
/// </summary>
public sealed class GoldBarOptions
{
    public const string SectionName = "GoldBar";

    public string Name { get; set; } = string.Empty;

    /// <summary>What was paid for the bar.</summary>
    public decimal PurchasePrice { get; set; }

    /// <summary>Profit targets in percent, e.g. 5 and 7: sell once a buyback price reaches purchase × (1 + target).</summary>
    public IList<decimal> ProfitTargetsPercent { get; init; } = [];

    /// <summary>True when any part of the section is present; a partial section is then reported, not ignored.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Name) || PurchasePrice != 0m || ProfitTargetsPercent.Count > 0;

    /// <summary>An absent section is valid (the panel is hidden); a present one must be complete.</summary>
    public bool IsValid =>
        !IsConfigured
        || (!string.IsNullOrWhiteSpace(Name)
            && PurchasePrice > 0m
            && ProfitTargetsPercent.Count > 0
            && ProfitTargetsPercent.All(target => target > 0m));
}
