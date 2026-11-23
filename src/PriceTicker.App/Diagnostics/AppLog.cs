using System.Globalization;
using Serilog;
using Serilog.Events;

namespace PriceTicker.App.Diagnostics;

/// <summary>
/// Where and how the app writes its log: one file per day under
/// <c>%LOCALAPPDATA%\PriceTicker\logs</c>, kept for <see cref="RetainedDays"/> days.
/// </summary>
internal static class AppLog
{
    public const int RetainedDays = 14;

    private const long MaxFileBytes = 10 * 1024 * 1024;

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>The folder holding the log files.</summary>
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceTicker", "logs");

    /// <summary>
    /// A logger that writes each event to disk straight away (no buffering),
    /// so the last lines before a crash are not lost.
    /// </summary>
    public static Serilog.Core.Logger Create() => Create(Folder);

    /// <inheritdoc cref="Create()"/>
    internal static Serilog.Core.Logger Create(string folder) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            // HttpClient logs every request and response at Information: five lines per price, every refresh.
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(folder, "priceticker-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                fileSizeLimitBytes: MaxFileBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: OutputTemplate,
                // Log files read the same on any machine.
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
}
