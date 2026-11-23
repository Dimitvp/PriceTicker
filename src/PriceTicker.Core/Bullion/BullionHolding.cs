namespace PriceTicker.Core.Bullion;

/// <summary>A bar that was bought, and the profit targets for selling it back.</summary>
public sealed record BullionHolding
{
    public BullionHolding(string name, decimal purchasePrice, IEnumerable<decimal> profitTargetsPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(purchasePrice);
        ArgumentNullException.ThrowIfNull(profitTargetsPercent);

        Name = name;
        PurchasePrice = purchasePrice;
        Targets = [.. profitTargetsPercent
            .Order()
            .Select(percent => new ProfitTarget(percent, Math.Round(purchasePrice * (1m + percent / 100m), 2)))];
    }

    public string Name { get; }

    public decimal PurchasePrice { get; }

    /// <summary>Lowest target first.</summary>
    public IReadOnlyList<ProfitTarget> Targets { get; }

    /// <summary>The highest target a buyback price reaches, or null if it reaches none.</summary>
    public ProfitTarget? HighestTargetReachedBy(decimal buybackPrice) =>
        Targets.LastOrDefault(target => buybackPrice >= target.BuybackPrice);
}

/// <summary>Sell once a dealer's buyback price is at least <see cref="BuybackPrice"/>.</summary>
public sealed record ProfitTarget(decimal Percent, decimal BuybackPrice);
