using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PriceTicker.Core.History;
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
    private decimal? _dailyChangePercent;
    private PriceHistory? _history;

    public QuoteItemViewModel(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        InstrumentId = instrument.Id;
        DisplayName = instrument.DisplayName;
    }

    public string InstrumentId { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private string _priceText = "—";

    /// <summary>Change over the selected range, or the daily change until a chart has loaded.</summary>
    [ObservableProperty]
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

        ShowChange();
    }

    /// <summary>Keeps the last price on screen, dimmed, rather than blanking the row.</summary>
    public void MarkUnavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsStale = _lastPrice is not null;
        Detail = reason;
    }

    private void ShowQuote(Quote quote)
    {
        var culture = CultureInfo.CurrentCulture;
        var price = quote.Price.ToString("N2", culture);
        PriceText = string.IsNullOrEmpty(quote.Currency) ? price : $"{price} {quote.Currency}";

        _lastPrice = quote.Price;
        _dailyChangePercent = quote.ChangePercent;
        ShowChange();

        IsStale = false;
        Detail = $"As of {quote.AsOf.ToLocalTime().ToString("dd.MM.yyyy HH:mm", culture)}";
    }

    private void ShowChange()
    {
        var hasChart = _lastPrice is not null && _history is not null;
        var change = hasChart ? _history!.ChangePercentTo(_lastPrice!.Value) : _dailyChangePercent;

        ChangeDescription = change is null ? null
            : hasChart ? $"Change since {_history!.Points[0].Time.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)}"
            : "Daily change (chart unavailable)";

        ChangeText = change is { } percent
            ? $"{percent.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)} %"
            : string.Empty;
        IsUp = change > 0m;
        IsDown = change < 0m;
    }
}
