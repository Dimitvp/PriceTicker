using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;
using PriceTicker.Core.Bullion;

namespace PriceTicker.App.ViewModels;

/// <summary>
/// The physical gold bar panel: each dealer's buy and buyback price, what was paid, and the
/// buyback prices at which selling gives the profit targets.
/// </summary>
public sealed class GoldBarViewModel : ObservableObject
{
    private readonly IBullionService _bullionService;
    private readonly BullionHolding? _holding;

    public GoldBarViewModel(IBullionService bullionService, IOptions<GoldBarOptions> options)
    {
        ArgumentNullException.ThrowIfNull(bullionService);
        ArgumentNullException.ThrowIfNull(options);

        _bullionService = bullionService;

        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return;
        }

        _holding = new BullionHolding(settings.Name, settings.PurchasePrice, settings.ProfitTargetsPercent);

        var culture = CultureInfo.CurrentCulture;
        Title = _holding.Name;
        PurchaseText = $"Bought {Formats.Euro(_holding.PurchasePrice)}";
        Targets = [.. _holding.Targets.Select(target =>
            $"+{target.Percent.ToString("0.##", culture)} %  at buyback ≥ {Formats.Euro(target.BuybackPrice)}")];
    }

    /// <summary>False when no gold bar is configured; the panel is then hidden.</summary>
    public bool IsVisible => _holding is not null;

    public string Title { get; } = string.Empty;

    public string PurchaseText { get; } = string.Empty;

    /// <summary>One line per profit target, lowest first.</summary>
    public IReadOnlyList<string> Targets { get; } = [];

    /// <summary>One row per dealer, in the order the dealers are registered.</summary>
    public ObservableCollection<DealerRowViewModel> Dealers { get; } = [];

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_holding is null)
        {
            return;
        }

        var results = await _bullionService.GetQuotesAsync(cancellationToken);
        foreach (var result in results)
        {
            RowFor(result.Dealer).Apply(result);
        }
    }

    private DealerRowViewModel RowFor(string dealer)
    {
        var row = Dealers.FirstOrDefault(existing => existing.Dealer == dealer);
        if (row is null)
        {
            row = new DealerRowViewModel(dealer, _holding!);
            Dealers.Add(row);
        }

        return row;
    }
}

/// <summary>One dealer's prices for the bar. Keeps the last prices, dimmed, when a refresh fails.</summary>
public sealed partial class DealerRowViewModel : ObservableObject
{
    private readonly BullionHolding _holding;
    private bool _hasPrices;

    public DealerRowViewModel(string dealer, BullionHolding holding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dealer);
        ArgumentNullException.ThrowIfNull(holding);

        Dealer = dealer;
        _holding = holding;
    }

    public string Dealer { get; }

    /// <summary>What you pay the dealer ("Продаваме").</summary>
    [ObservableProperty]
    private string _buyText = "—";

    /// <summary>What the dealer pays you ("Купуваме"): the price the targets compare against.</summary>
    [ObservableProperty]
    private string _buybackText = "—";

    /// <summary>The buyback price has reached at least the lowest profit target.</summary>
    [ObservableProperty]
    private bool _isTargetReached;

    [ObservableProperty]
    private bool _isStale;

    /// <summary>Tooltip: when the prices were read and the profit if sold now, or why they are missing.</summary>
    [ObservableProperty]
    private string _detail = "Waiting for first update.";

    public void Apply(BullionQuoteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result)
        {
            case BullionQuoteAvailable available:
                Show(available.Quote);
                break;
            case BullionQuoteUnavailable unavailable:
                IsStale = _hasPrices;
                Detail = unavailable.Reason;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.GetType().Name, "Unsupported bullion result.");
        }
    }

    private void Show(BullionQuote quote)
    {
        var culture = CultureInfo.CurrentCulture;
        BuyText = Formats.Euro(quote.BuyPrice);
        BuybackText = Formats.Euro(quote.BuybackPrice);

        var reached = _holding.HighestTargetReachedBy(quote.BuybackPrice);
        IsTargetReached = reached is not null;

        var profit = quote.BuybackPrice - _holding.PurchasePrice;
        var profitPercent = profit / _holding.PurchasePrice * 100m;
        var verdict = reached is null
            ? string.Empty
            : $"{Environment.NewLine}Target +{reached.Percent.ToString("0.##", culture)} % reached.";

        Detail = $"Read {Formats.Timestamp(quote.AsOf)}{Environment.NewLine}"
            + $"Selling now: {Formats.Signed(profit)} EUR ({Formats.Signed(profitPercent)} %){verdict}";

        _hasPrices = true;
        IsStale = false;
    }
}
