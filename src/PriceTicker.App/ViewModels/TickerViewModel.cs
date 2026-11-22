using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.App.ViewModels;

/// <summary>State and behaviour of the ticker window.</summary>
public sealed partial class TickerViewModel : ObservableObject
{
    private readonly IQuoteService _quoteService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TickerViewModel> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly IReadOnlyList<Instrument> _instruments;
    private readonly Dictionary<string, QuoteItemViewModel> _itemsById;
    private bool _isRefreshing;

    public TickerViewModel(
        IQuoteService quoteService,
        IOptions<TickerOptions> options,
        TimeProvider timeProvider,
        ILogger<TickerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(quoteService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _quoteService = quoteService;
        _timeProvider = timeProvider;
        _logger = logger;

        var settings = options.Value;
        _refreshInterval = settings.RefreshInterval;
        _instruments = [.. settings.Instruments.Select(instrument => instrument.ToInstrument())];

        Items = [.. _instruments.Select(instrument => new QuoteItemViewModel(instrument))];
        _itemsById = Items.ToDictionary(item => item.InstrumentId, StringComparer.Ordinal);
    }

    public IReadOnlyList<QuoteItemViewModel> Items { get; }

    /// <summary>When at least one price last arrived. Unchanged by refreshes that got nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastUpdatedText))]
    private DateTimeOffset? _lastUpdated;

    [ObservableProperty]
    private bool _isAlwaysOnTop = true;

    public string LastUpdatedText => LastUpdated is { } updated
        ? $"Updated {updated.ToString("HH:mm", CultureInfo.CurrentCulture)}"
        : "Loading…";

    /// <summary>
    /// Refreshes immediately, then on every tick of the configured interval until cancelled.
    /// A failed round is logged and shown, and never stops later rounds.
    /// Call it from the UI thread so updates resume there.
    /// </summary>
    public async Task RunAutoRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshSafelyAsync(cancellationToken);

            using var timer = new PeriodicTimer(_refreshInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshSafelyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Fetches all prices once. A call made while another is running is ignored.</summary>
    [RelayCommand]
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var results = await _quoteService.GetQuotesAsync(_instruments, cancellationToken);
            foreach (var result in results)
            {
                if (_itemsById.TryGetValue(result.Instrument.Id, out var item))
                {
                    item.Apply(result);
                }
            }

            if (results.Any(result => result is QuoteAvailable))
            {
                LastUpdated = _timeProvider.GetLocalNow();
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Without this, one unexpected error would end the loop and freeze the widget silently.
            LogRefreshFailed(ex);
            foreach (var item in Items)
            {
                item.MarkUnavailable("Refresh failed.");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Price refresh failed.")]
    private partial void LogRefreshFailed(Exception exception);
}
