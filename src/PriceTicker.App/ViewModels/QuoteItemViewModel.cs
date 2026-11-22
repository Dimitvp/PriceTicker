using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.App.ViewModels;

/// <summary>One row of the widget. Keeps the last known price when a refresh fails.</summary>
public sealed partial class QuoteItemViewModel : ObservableObject
{
    private bool _hasPrice;

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

    [ObservableProperty]
    private string _changeText = string.Empty;

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

    /// <summary>Keeps the last price on screen, dimmed, rather than blanking the row.</summary>
    public void MarkUnavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsStale = _hasPrice;
        Detail = reason;
    }

    private void ShowQuote(Quote quote)
    {
        var culture = CultureInfo.CurrentCulture;
        var price = quote.Price.ToString("N2", culture);

        PriceText = string.IsNullOrEmpty(quote.Currency) ? price : $"{price} {quote.Currency}";

        var change = quote.ChangePercent;
        ChangeText = change is { } percent
            ? $"{percent.ToString("+0.00;-0.00;0.00", culture)} %"
            : string.Empty;
        IsUp = change > 0m;
        IsDown = change < 0m;

        _hasPrice = true;
        IsStale = false;
        Detail = $"As of {quote.AsOf.ToLocalTime().ToString("dd.MM.yyyy HH:mm", culture)}";
    }
}
