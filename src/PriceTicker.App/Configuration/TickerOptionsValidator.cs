using Microsoft.Extensions.Options;

namespace PriceTicker.App.Configuration;

/// <summary>
/// Rules that data annotations cannot express. Runs on first access to the options, so a
/// broken appsettings.json stops the app at startup with a clear message.
/// </summary>
public sealed class TickerOptionsValidator : IValidateOptions<TickerOptions>
{
    /// <summary>
    /// Floor that keeps the free Twelve Data plan (8 credits a minute, 800 a day) within limits.
    /// </summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(1);

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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAnyBlank(InstrumentOptions instrument) =>
        string.IsNullOrWhiteSpace(instrument.Id)
        || string.IsNullOrWhiteSpace(instrument.DisplayName)
        || string.IsNullOrWhiteSpace(instrument.Symbol)
        || string.IsNullOrWhiteSpace(instrument.Provider);
}
