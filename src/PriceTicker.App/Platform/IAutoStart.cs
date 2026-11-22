namespace PriceTicker.App.Platform;

/// <summary>Whether the app starts when the user signs in to the operating system.</summary>
public interface IAutoStart
{
    /// <summary>False where the platform or launch mode has no supported mechanism.</summary>
    bool IsSupported { get; }

    /// <summary>True when sign-in starts this very executable.</summary>
    bool IsEnabled { get; }

    /// <summary>Turns auto-start on or off. Returns false when the system refused the change.</summary>
    bool TrySetEnabled(bool enabled);
}
