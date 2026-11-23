using System.Reflection;
using Avalonia;
using PriceTicker.App.Diagnostics;
using PriceTicker.App.Platform;
using Serilog;

namespace PriceTicker.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Logger = AppLog.Create();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        SingleInstance? instance = null;
        try
        {
            instance = SingleInstance.Acquire();
            if (!instance.IsFirst)
            {
                Log.Information("PriceTicker is already running; asked it to show its window.");
                return 0;
            }

            Log.Information(
                "PriceTicker {Version} starting from {Path}.",
                Assembly.GetExecutingAssembly().GetName().Version,
                Environment.ProcessPath);

            return BuildAvaloniaApp()
                .AfterSetup(builder => ((App)builder.Instance!).ShowOnRequestFrom(instance))
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A WinExe has no console, and Avalonia may be the thing that failed: log, then tell the user natively.
            Log.Fatal(ex, "PriceTicker crashed.");
            NativeMessageBox.ShowError(
                "PriceTicker crashed",
                // The innermost message names the real cause, e.g. the missing DLL behind a TypeInitializationException.
                $"{ex.GetBaseException().Message}{Environment.NewLine}{Environment.NewLine}Details are in the log:{Environment.NewLine}{AppLog.Folder}");
            return 1;
        }
        finally
        {
            if (instance?.IsFirst == true)
            {
                Log.Information("PriceTicker stopped.");
            }

            instance?.Dispose();
            Log.CloseAndFlush();
        }
    }

    // Also used by the Avalonia designer; keep it free of side effects.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Raised on a background thread just before the process dies: the last chance to write it down.
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; PriceTicker is terminating.");
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "A background task failed without anyone awaiting it.");
        e.SetObserved();
    }
}
