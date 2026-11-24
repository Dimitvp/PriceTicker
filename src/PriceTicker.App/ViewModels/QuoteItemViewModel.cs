using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PriceTicker.Core.History;
using PriceTicker.Core.Holdings;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.App.ViewModels;

/// <summary>
/// One row of the widget: price, chart and change over the selected range.
/// Keeps the last known price when a refresh fails.
/// </summary>
public sealed partial class QuoteItemViewModel : ObservableObject
{
    private decimal? _lastPrice;
    private string _currency = string.Empty;
    private decimal? _dailyChangePercent;
    private PriceHistory? _history;
    private readonly Holding? _holding;
    private readonly TimeProvider _clock;
    private Market? _market;

    public QuoteItemViewModel(Instrument instrument, Holding? holding = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        _clock = clock ?? TimeProvider.System;
        InstrumentId = instrument.Id;
        DisplayName = instrument.DisplayName;
        IsinText = instrument.Isin?.Value ?? string.Empty;
        _holding = holding;
        AlertDescription = holding is null ? null : DescribeAlerts(holding.Alerts, AlertLevel.None);
    }

    public string InstrumentId { get; }

    /// <summary>A position (quantity and money put in) is configured, so the three position columns have values.</summary>
    public bool HasPosition => _holding?.Position is not null;

    public string DisplayName { get; }

    /// <summary>"IE00B5BMR087"; empty for instruments without an ISIN (Bitcoin).</summary>
    public string IsinText { get; }

    /// <summary>The grey line under the name: the ISIN and, for a one-off purchase, the "Bought" text.</summary>
    public string SubtitleText => string.Join(" · ", new[] { IsinText, HoldingText }.Where(part => part.Length > 0));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownPriceText))]
    private string _priceText = "—";

    /// <summary>"Bought 100.00 · +5.00 %" under the name; empty without a one-off purchase price.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _holdingText = string.Empty;

    /// <summary>"1,608.51": the money put into the position so far; empty when no position is configured.</summary>
    [ObservableProperty]
    private string _investedText = string.Empty;

    /// <summary>"1,580.15": what selling the whole position now would bring, before the sale's costs.</summary>
    [ObservableProperty]
    private string _positionValueText = string.Empty;

    /// <summary>"-1.76 %": the whole position against what was put in.</summary>
    [ObservableProperty]
    private string _positionGainText = string.Empty;

    /// <summary>"10.03.2030": the first purchase; empty when not configured.</summary>
    [ObservableProperty]
    private string _heldSinceText = string.Empty;

    /// <summary>"6 mo 9 d": how long the position has been held.</summary>
    [ObservableProperty]
    private string _heldForText = string.Empty;

    [ObservableProperty]
    private bool _isPositionUp;

    [ObservableProperty]
    private bool _isPositionDown;

    /// <summary>The review alert (−20%) has been reached: the row turns yellow.</summary>
    [ObservableProperty]
    private bool _isReviewAlert;

    /// <summary>A sell alert (−40% or +50%) has been reached: the row is highlighted as urgent.</summary>
    [ObservableProperty]
    private bool _isSellAlert;

    /// <summary>"REVIEW" or "SELL" next to the name, so the alert never depends on colour alone.</summary>
    [ObservableProperty]
    private string _alertBadge = string.Empty;

    /// <summary>"XETRA · open", "Milan · opens 10:00", "Paris · closed"; empty when the provider does not say.</summary>
    [ObservableProperty]
    private string _marketText = string.Empty;

    [ObservableProperty]
    private bool _isMarketOpen;

    /// <summary>Tooltip: the alert levels and which one, if any, has been reached.</summary>
    [ObservableProperty]
    private string? _alertDescription;

    /// <summary>Change over the selected range, or the daily change until a chart has loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownChangeText))]
    private string _changeText = string.Empty;

    /// <summary>Tooltip for the change: what it is measured against, so a fallback never passes for a range change.</summary>
    [ObservableProperty]
    private string? _changeDescription;

    [ObservableProperty]
    private bool _isUp;

    [ObservableProperty]
    private bool _isDown;

    /// <summary>True while the shown price is older than the last refresh attempt.</summary>
    [ObservableProperty]
    private bool _isStale;

    /// <summary>Tooltip text: when the price is from, or why it could not be refreshed.</summary>
    [ObservableProperty]
    private string _detail = "Waiting for first update.";

    /// <summary>Prices over the selected range, oldest first; empty when there is no chart.</summary>
    [ObservableProperty]
    private IReadOnlyList<double> _chartValues = [];

    /// <summary>Tooltip text for the chart: why it is missing, if it is.</summary>
    [ObservableProperty]
    private string? _chartStatus;

    /// <summary>Index into <see cref="ChartValues"/> of the point under the pointer, set by the chart.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHovering), nameof(ShownPriceText), nameof(ShownChangeText))]
    private int? _hoveredIndex;

    public bool IsHovering => HoveredPoint is not null;

    /// <summary>The price shown in the row: the hovered point's while hovering, otherwise the live one.</summary>
    public string ShownPriceText => HoveredPoint is { } hovered ? FormatPrice(hovered.Point.Price) : PriceText;

    /// <summary>The change column: the hovered point's date while hovering, otherwise the change.</summary>
    public string ShownChangeText => HoveredPoint is { } hovered ? FormatPointTime(hovered.Point, hovered.Range) : ChangeText;

    /// <summary>
    /// The widest price this row can show (live or any chart point). Laid out invisibly in the
    /// price cell so that hovering never changes the column's, and so the window's, width.
    /// </summary>
    [ObservableProperty]
    private string _widestPriceText = string.Empty;

    private (PricePoint Point, ChartRange Range)? HoveredPoint =>
        HoveredIndex is { } index && _history is { } history && index >= 0 && index < history.Points.Count
            ? (history.Points[index], history.Range)
            : null;

    public void Apply(QuoteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result)
        {
            case QuoteAvailable available:
                ShowQuote(available.Quote);
                break;
            case QuoteUnavailable unavailable:
                MarkUnavailable(unavailable.Reason);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.GetType().Name, "Unsupported quote result.");
        }
    }

    public void ApplyHistory(HistoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result)
        {
            case HistoryAvailable available:
                _history = available.History;
                ChartValues = [.. available.History.Points.Select(point => (double)point.Price)];
                ChartStatus = null;
                break;
            case HistoryUnavailable unavailable:
                // A chart for another range would be misleading, so it is cleared, not kept.
                _history = null;
                ChartValues = [];
                ChartStatus = unavailable.Reason;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.GetType().Name, "Unsupported history result.");
        }

        // The hovered index referred to the previous chart.
        HoveredIndex = null;
        UpdateWidestPrice();
        ShowChange();
    }

    /// <summary>Keeps the last price on screen, dimmed, rather than blanking the row.</summary>
    public void MarkUnavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsStale = _lastPrice is not null;
        Detail = reason;

        // The open/closed state was a snapshot of the last good refresh; it would go wrong while we can't refresh.
        MarketText = _market?.Exchange ?? string.Empty;
        IsMarketOpen = false;
    }

    private void ShowQuote(Quote quote)
    {
        _currency = quote.Currency;
        PriceText = FormatPrice(quote.Price);

        _lastPrice = quote.Price;
        UpdateWidestPrice();
        ShowHolding(quote.Price);
        _dailyChangePercent = quote.ChangePercent;
        ShowChange();

        IsStale = false;
        Detail = $"As of {Formats.Timestamp(quote.AsOf)}";
        _market = quote.Market;
        MarketText = DescribeMarket(quote.Market);
        IsMarketOpen = quote.Market?.State == MarketState.Open;
    }

    private void ShowPosition(Position? position, decimal price)
    {
        if (position is null)
        {
            return;
        }

        var gain = position.GainPercent(price);
        InvestedText = Formats.Price(position.Invested);
        PositionValueText = Formats.Price(position.ValueAt(price));
        PositionGainText = $"{Formats.Signed(gain)} %";
        IsPositionUp = gain > 0m;
        IsPositionDown = gain < 0m;

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (position is { FirstPurchase: { } first } && position.HeldFor(today) is { } heldFor)
        {
            HeldSinceText = Formats.Date(first);
            HeldForText = Formats.Period(heldFor);
        }
    }

    private void ShowHolding(decimal price)
    {
        if (_holding is null)
        {
            return;
        }

        HoldingText = _holding.PurchasePrice is { } purchasePrice
            ? $"Bought {Formats.ExactPrice(purchasePrice)} · {Formats.Signed(_holding.GainPercent(price)!.Value)} %"
            : string.Empty;
        ShowPosition(_holding.Position, price);

        var level = _holding.Alerts.Evaluate(price);
        IsReviewAlert = level == AlertLevel.Review;
        IsSellAlert = level == AlertLevel.Sell;
        AlertBadge = level switch
        {
            AlertLevel.Review => "REVIEW",
            AlertLevel.Sell => "SELL",
            _ => string.Empty,
        };
        AlertDescription = DescribeAlerts(_holding.Alerts, level);
    }

    private string DescribeMarket(Market? market)
    {
        if (market is null)
        {
            return string.Empty;
        }

        var state = market switch
        {
            { State: MarketState.Open } => "open",
            { State: MarketState.BeforeOpen, OpensAt: { } opensAt } => $"opens {Formats.OpeningTime(opensAt, _clock.GetUtcNow())}",
            { State: MarketState.Closed } => "closed",
            _ => string.Empty,
        };

        return string.Join(" · ", new[] { market.Exchange, state }.Where(part => part.Length > 0));
    }

    private static string DescribeAlerts(PriceAlerts alerts, AlertLevel level)
    {
        var culture = CultureInfo.CurrentCulture;
        string Describe(string label, string comparison, decimal? value) =>
            value is { } price ? $"{label} {comparison} {Formats.ExactPrice(price)}" : string.Empty;

        var levels = string.Join(" · ", new[]
        {
            Describe("Review", "≤", alerts.ReviewAtOrBelow),
            Describe("Exit", "≤", alerts.SellAtOrBelow),
            Describe("Sell half", "≥", alerts.SellAtOrAbove),
        }.Where(text => text.Length > 0));

        var status = level switch
        {
            AlertLevel.Review => "Review alert reached: check whether the thesis still holds.",
            AlertLevel.Sell => "Sell alert reached.",
            _ => null,
        };

        if (levels.Length == 0)
        {
            return status ?? "No alerts set.";
        }

        return status is null ? $"Alerts: {levels}" : $"{status}{Environment.NewLine}Alerts: {levels}";
    }

    private void UpdateWidestPrice()
    {
        var prices = (_history?.Points.Select(point => point.Price) ?? []).Append(_lastPrice ?? 0m);
        WidestPriceText = prices.Select(FormatPrice).MaxBy(text => text.Length) ?? string.Empty;
    }

    private string FormatPrice(decimal price)
    {
        var text = Formats.Price(price);
        return string.IsNullOrEmpty(_currency) ? text : $"{text} {_currency}";
    }

    // Intraday ranges need the time; longer ones are one point per day.
    private static string FormatPointTime(PricePoint point, ChartRange range) =>
        point.Time.ToLocalTime().ToString(
            range is ChartRange.Day or ChartRange.Week ? "dd.MM HH:mm" : "dd.MM.yyyy",
            CultureInfo.CurrentCulture);

    private static string DescribeChartChange(PriceHistory history) =>
        history.HasPreviousClose
            ? $"Change since the close before {Formats.Timestamp(history.Points[0].Time)}"
            : $"Change since {Formats.Timestamp(history.Points[0].Time)}";

    private void ShowChange()
    {
        // 1D uses the provider's own daily change: it is measured from the official close (the closing
        // auction), which the last intraday bar of the chart can miss by a few tenths of a percent.
        var isDaily = _history is null || (_history.Range == ChartRange.Day && _dailyChangePercent is not null);
        var change = isDaily ? _dailyChangePercent
            : _lastPrice is { } price ? _history!.ChangePercentTo(price)
            : null;

        ChangeDescription = change is null ? null
            : !isDaily ? DescribeChartChange(_history!)
            : _history is null ? "Daily change (chart unavailable)"
            : "Change since the previous close";

        ChangeText = change is { } percent
            ? $"{Formats.Signed(percent)} %"
            : string.Empty;
        IsUp = change > 0m;
        IsDown = change < 0m;
    }
}
