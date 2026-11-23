using Microsoft.Extensions.Options;

namespace PriceTicker.App.Configuration;

/// <summary>
/// Rules that data annotations cannot express. Runs on first access to the options, so a
/// broken appsettings.json stops the app at startup with a clear message.
/// </summary>
public sealed class TickerOptionsValidator : IValidateOptions<TickerOptions>
{
    /// <summary>
    /// Floor that keeps one Twelve Data instrument within the free plan: each refresh costs a quote
    /// credit and a chart credit, so every 4 minutes is about 720 of the 800 credits a day.
    /// </summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(4);

    public ValidateOptionsResult Validate(string? name, TickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.RefreshInterval < MinimumRefreshInterval)
        {
            failures.Add($"Ticker:RefreshInterval must be at least {MinimumRefreshInterval}.");
        }

        if (options.Instruments.Count == 0)
        {
            failures.Add("Ticker:Instruments must list at least one instrument.");
        }

        failures.AddRange(options.Instruments
            .Select((instrument, index) => (instrument, index))
            .Where(entry => IsAnyBlank(entry.instrument))
            .Select(entry => $"Ticker:Instruments:{entry.index} needs Id, DisplayName, Symbol and Provider."));

        failures.AddRange(options.Instruments
            .Where(instrument => !string.IsNullOrWhiteSpace(instrument.Id))
            .GroupBy(instrument => instrument.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"Ticker:Instruments has duplicate Id '{group.Key}'."));

        var instrumentIds = options.Instruments.Select(instrument => instrument.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        failures.AddRange(options.Holdings
            .Where(holding => !instrumentIds.Contains(holding.Key))
            .Select(holding => $"Ticker:Holdings:{holding.Key} does not match any instrument Id."));
        failures.AddRange(options.Holdings
            .Where(holding => holding.Value.PurchasePrice <= 0m)
            .Select(holding => $"Ticker:Holdings:{holding.Key}:PurchasePrice must be greater than zero."));

        failures.AddRange(ValidateGroups(options.Groups, instrumentIds));

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static List<string> ValidateGroups(IList<GroupOptions> groups, HashSet<string> instrumentIds)
    {
        var failures = groups
            .Select((group, index) => (group, index))
            .Where(entry => string.IsNullOrWhiteSpace(entry.group.Title) || entry.group.Instruments.Count == 0)
            .Select(entry => $"Ticker:Groups:{entry.index} needs a Title and at least one instrument Id.")
            .ToList();

        var grouped = groups.SelectMany(group => group.Instruments).ToList();
        failures.AddRange(grouped
            .Where(id => !instrumentIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => $"Ticker:Groups lists '{id}', which does not match any instrument Id."));
        failures.AddRange(grouped
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(sameId => sameId.Count() > 1)
            .Select(sameId => $"Ticker:Groups lists '{sameId.Key}' more than once."));

        return failures;
    }

    private static bool IsAnyBlank(InstrumentOptions instrument) =>
        string.IsNullOrWhiteSpace(instrument.Id)
        || string.IsNullOrWhiteSpace(instrument.DisplayName)
        || string.IsNullOrWhiteSpace(instrument.Symbol)
        || string.IsNullOrWhiteSpace(instrument.Provider);
}
