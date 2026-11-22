using System.Security;
using Microsoft.Extensions.Logging;

namespace PriceTicker.App.Platform;

/// <summary>
/// Windows auto-start through the current user's <c>Run</c> registry key: no admin rights
/// needed, and the user can also see or remove it in Task Manager → Startup apps.
/// </summary>
internal sealed partial class RunKeyAutoStart : IAutoStart
{
    internal const string ValueName = "PriceTicker";

    private readonly IRunKey _runKey;
    private readonly string _command;
    private readonly ILogger<RunKeyAutoStart> _logger;

    public RunKeyAutoStart(IRunKey runKey, string executablePath, ILogger<RunKeyAutoStart> logger)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(logger);

        _runKey = runKey;
        // Quoted, because the path may contain spaces (e.g. "D:\01. Work\...").
        _command = $"\"{executablePath}\"";
        _logger = logger;
    }

    public bool IsSupported => true;

    // An entry pointing at an older copy of the exe does not count: turning the option on
    // again rewrites it to this executable.
    public bool IsEnabled
    {
        get
        {
            try
            {
                return string.Equals(_runKey.GetValue(ValueName), _command, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (IsRegistryRefusal(ex))
            {
                // Read at start-up: a locked-down profile must not stop the app from opening.
                LogReadFailed(ex);
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _runKey.SetValue(ValueName, _command);
            }
            else
            {
                _runKey.DeleteValue(ValueName);
            }

            return true;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            LogChangeFailed(ex, enabled);
            return false;
        }
    }

    private static bool IsRegistryRefusal(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the start with Windows setting.")]
    private partial void LogReadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set start with Windows to {Enabled}.")]
    private partial void LogChangeFailed(Exception exception, bool enabled);
}
