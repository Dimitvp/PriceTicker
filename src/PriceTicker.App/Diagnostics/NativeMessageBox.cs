using System.Runtime.InteropServices;

namespace PriceTicker.App.Diagnostics;

/// <summary>
/// The Windows message box, for failures that happen before Avalonia can draw anything
/// (for example a missing native graphics library). Does nothing on other systems.
/// </summary>
internal static partial class NativeMessageBox
{
    private const uint IconError = 0x10;

    public static void ShowError(string title, string text)
    {
        if (OperatingSystem.IsWindows())
        {
            _ = MessageBoxW(IntPtr.Zero, text, title, IconError);
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
