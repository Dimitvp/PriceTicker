namespace PriceTicker.App.Platform;

/// <summary>Used where auto-start is not implemented (macOS, Linux, or when run through <c>dotnet</c>).</summary>
internal sealed class UnsupportedAutoStart : IAutoStart
{
    public bool IsSupported => false;

    public bool IsEnabled => false;

    public bool TrySetEnabled(bool enabled) => false;
}
