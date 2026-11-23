using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;
using PriceTicker.App.Platform;
using PriceTicker.Core.History;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;

namespace PriceTicker.App.ViewModels;

/// <summary>State and behaviour of the ticker window.</summary>
public sealed partial class TickerViewModel : ObservableObject, IDisposable
{
    private readonly IQuoteService _quoteService;
    private readonly IPriceHistoryService _historyService;
    private readonly IAutoStart _autoStart;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TickerViewModel> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly IReadOnlyList<Instrument> _instruments;
    private readonly Dictionary<string, QuoteItemViewModel> _itemsById;
    private bool _isRefreshing;
    private bool _isRevertingStartsWithWindows;
    private CancellationTokenSource? _rangeChange;
    private readonly Dictionary<ChartRange, LoadedCharts> _chartCache = [];
    private bool _isDisposed;

    /// <summary>
    /// Long-range charts move slowly, so they are re-fetched at most this often rather than on
    /// every refresh. This keeps Twelve Data's free plan (800 credits a day) well within limits.
    /// </summary>
    internal static readonly TimeSpan LongRangeChartMaxAge = TimeSpan.FromHours(1);

    public TickerViewModel(
        IQuoteService quoteService,
        IPriceHistoryService historyService,
        GoldBarViewModel goldBar,
        IOptions<TickerOptions> options,
        TimeProvider timeProvider,
        IAutoStart autoStart,
        ILogger<TickerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(quoteService);
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(goldBar);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _quoteService = quoteService;
        _historyService = historyService;
        GoldBar = goldBar;
        _timeProvider = timeProvider;
        _autoStart = autoStart;
        _logger = logger;

        // Field, not property: reading the current state must not write it back.
        _startsWithWindows = autoStart.IsEnabled;

        var settings = options.Value;
        _refreshInterval = settings.RefreshInterval;
        _instruments = [.. settings.Instruments.Select(instrument => instrument.ToInstrument())];

        Items = [.. _instruments.Select(instrument =>
            new QuoteItemViewModel(instrument, settings.Holdings.TryGetValue(instrument.Id, out var holding) ? holding.ToHolding() : null, timeProvider))];
        _itemsById = Items.ToDictionary(item => item.InstrumentId, StringComparer.Ordinal);
        Groups = BuildGroups(settings.Groups, Items);
    }

    public IReadOnlyList<QuoteItemViewModel> Items { get; }

    /// <summary>At least one row has a position, so the Invested / Value / Gain headings are shown.</summary>
    public bool ShowsPositions => Items.Any(item => item.HasPosition);

    /// <summary>The rows as shown: the configured groups in order, then any ungrouped rows.</summary>
    public IReadOnlyList<QuoteGroupViewModel> Groups { get; }

    /// <summary>The physical gold bar panel (hidden when no bar is configured).</summary>
    public GoldBarViewModel GoldBar { get; }

    public static IReadOnlyList<ChartRangeOption> Ranges { get; } =
    [
        new(ChartRange.Day, "1D"),
        new(ChartRange.Week, "1W"),
        new(ChartRange.ThreeMonths, "3M"),
        new(ChartRange.SixMonths, "6M"),
        new(ChartRange.Year, "1Y"),
    ];

    /// <summary>The range every chart shows; changing it reloads the charts at once.</summary>
    [ObservableProperty]
    private ChartRangeOption _selectedRange = Ranges[0];

    /// <summary>When at least one price last arrived. Unchanged by refreshes that got nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastUpdatedText))]
    private DateTimeOffset? _lastUpdated;

    [ObservableProperty]
    private bool _isAlwaysOnTop = true;

    /// <summary>Bound to the "Start with Windows" menu item; changing it updates the system.</summary>
    [ObservableProperty]
    private bool _startsWithWindows;

    public bool IsAutoStartSupported => _autoStart.IsSupported;

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

    /// <summary>
    /// Fetches all prices and, when due, the charts for the selected range, in parallel.
    /// A call made while another is running is ignored.
    /// </summary>
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
            var range = SelectedRange.Range;
            var quotesTask = _quoteService.GetQuotesAsync(_instruments, cancellationToken);
            var historyTask = AreChartsDue(range)
                ? _historyService.GetHistoryAsync(_instruments, range, cancellationToken)
                : null;
            // Dealer pages are slower than the price APIs: started now, awaited last, so they never hold back prices.
            var goldBarTask = GoldBar.RefreshAsync(cancellationToken);
            await Task.WhenAll(historyTask is null ? [quotesTask] : [quotesTask, historyTask]);

            var quotes = await quotesTask;
            foreach (var result in quotes)
            {
                ItemFor(result)?.Apply(result);
            }

            if (historyTask is not null)
            {
                ApplyHistory(await historyTask, range);
            }

            if (quotes.Any(result => result is QuoteAvailable))
            {
                LastUpdated = _timeProvider.GetLocalNow();
            }

            await goldBarTask;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>Loads the charts for <paramref name="range"/> only; prices are left as they are.</summary>
    internal async Task LoadHistoryAsync(ChartRange range, CancellationToken cancellationToken)
    {
        var results = await _historyService.GetHistoryAsync(_instruments, range, cancellationToken);
        ApplyHistory(results, range);
    }

    partial void OnSelectedRangeChanged(ChartRangeOption value)
    {
        if (_isDisposed)
        {
            return;
        }

        // Switching back to a range seen recently costs no request: Twelve Data's free plan allows
        // only 8 a minute, and clicking through the ranges would otherwise use them up.
        if (_chartCache.TryGetValue(value.Range, out var cached) && !IsExpired(cached, MaxChartAge(value.Range)))
        {
            ApplyHistory(cached.Results, value.Range);
            return;
        }

        _ = LoadHistorySafelyAsync(value.Range);
    }

    // A range clicked while an older one is still loading supersedes it.
    private async Task LoadHistorySafelyAsync(ChartRange range)
    {
        _rangeChange?.Cancel();
        var rangeChange = _rangeChange = new CancellationTokenSource();

        try
        {
            await LoadHistoryAsync(range, rangeChange.Token);
        }
        catch (OperationCanceledException) when (rangeChange.IsCancellationRequested)
        {
            // Superseded by a newer range.
        }
        catch (Exception ex)
        {
            LogHistoryFailed(ex);
        }
        finally
        {
            // Each load owns its source. Clear the field only if no newer load replaced it.
            if (ReferenceEquals(_rangeChange, rangeChange))
            {
                _rangeChange = null;
            }

            rangeChange.Dispose();
        }
    }

    // On refresh, short ranges are always re-fetched; long ones only once they are an hour old.
    private bool AreChartsDue(ChartRange range) =>
        IsShortRange(range)
        || !_chartCache.TryGetValue(range, out var cached)
        || IsExpired(cached, LongRangeChartMaxAge);

    private TimeSpan MaxChartAge(ChartRange range) => IsShortRange(range) ? _refreshInterval : LongRangeChartMaxAge;

    private static bool IsShortRange(ChartRange range) => range is ChartRange.Day or ChartRange.Week;

    private bool IsExpired(LoadedCharts charts, TimeSpan maxAge) => _timeProvider.GetUtcNow() - charts.LoadedAt >= maxAge;

    private void ApplyHistory(IReadOnlyList<HistoryResult> results, ChartRange range)
    {
        // Results can arrive after the user picked another range; those are stale.
        if (range != SelectedRange.Range)
        {
            return;
        }

        foreach (var result in results)
        {
            ItemFor(result)?.ApplyHistory(result);
        }

        // Only complete sets are reused; a missing chart is retried on the next request.
        if (results.All(result => result is HistoryAvailable))
        {
            _chartCache[range] = new LoadedCharts(results, _timeProvider.GetUtcNow());
        }
        else
        {
            _chartCache.Remove(range);
        }
    }

    /// <summary>Cancels a chart load still in flight; the container calls this on exit.</summary>
    public void Dispose()
    {
        _isDisposed = true;
        _rangeChange?.Cancel();
        _rangeChange?.Dispose();
        _rangeChange = null;
    }

    private QuoteItemViewModel? ItemFor(InstrumentResult result) =>
        _itemsById.GetValueOrDefault(result.Instrument.Id);

    // Tray menu items do not write their check state back, so they flip the setting through commands.
    [RelayCommand]
    private void ToggleAlwaysOnTop() => IsAlwaysOnTop = !IsAlwaysOnTop;

    [RelayCommand]
    private void ToggleStartsWithWindows() => StartsWithWindows = !StartsWithWindows;

    partial void OnStartsWithWindowsChanged(bool value)
    {
        if (_isRevertingStartsWithWindows)
        {
            return;
        }

        // Always applied, even when it looks like a no-op: unticking also clears an entry
        // left behind by an older copy of the exe.
        if (_autoStart.TrySetEnabled(value))
        {
            return;
        }

        // The system refused: show the real state rather than a checkbox that lies.
        _isRevertingStartsWithWindows = true;
        try
        {
            StartsWithWindows = _autoStart.IsEnabled;
        }
        finally
        {
            _isRevertingStartsWithWindows = false;
        }
    }

    private static List<QuoteGroupViewModel> BuildGroups(IEnumerable<GroupOptions> groups, IReadOnlyList<QuoteItemViewModel> items)
    {
        // Case-insensitive like the rest of the configuration; the validator has already rejected unknown Ids.
        var byId = items.ToDictionary(item => item.InstrumentId, StringComparer.OrdinalIgnoreCase);

        List<QuoteGroupViewModel> result = [.. groups.Select(group =>
            new QuoteGroupViewModel(group.Title, group.Note, [.. group.Instruments.Select(id => byId[id])]))];

        var grouped = result.SelectMany(group => group.Items).ToHashSet();
        var rest = items.Where(item => !grouped.Contains(item)).ToList();
        if (rest.Count > 0)
        {
            result.Add(new QuoteGroupViewModel(title: null, note: null, rest));
        }

        return result;
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

    private sealed record LoadedCharts(IReadOnlyList<HistoryResult> Results, DateTimeOffset LoadedAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Price refresh failed.")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading charts failed.")]
    private partial void LogHistoryFailed(Exception exception);
}
