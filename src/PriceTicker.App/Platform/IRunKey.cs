using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PriceTicker.App.Platform;

/// <summary>The string values of a <c>Run</c> registry key. Separated out so auto-start logic is testable.</summary>
internal interface IRunKey
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    /// <summary>Removes the value; does nothing when it is absent.</summary>
    void DeleteValue(string name);
}

[SupportedOSPlatform("windows")]
internal sealed class CurrentUserRunKey : IRunKey
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
