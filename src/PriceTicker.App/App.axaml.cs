using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PriceTicker.App.Composition;
using PriceTicker.App.Diagnostics;
using PriceTicker.App.Platform;
using PriceTicker.App.ViewModels;
using PriceTicker.App.Views;
using Serilog;

namespace PriceTicker.App;

[SuppressMessage("Design", "CA1001", Justification = "Avalonia owns the Application lifetime; fields are disposed in OnExit.")]
public sealed partial class App : Application
{
    private ServiceProvider? _services;
    private CancellationTokenSource? _shutdown;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private TickerViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow();
            desktop.Exit += OnExit;
            _desktop = desktop;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window CreateMainWindow()
    {
        TickerViewModel viewModel;
        try
        {
            _services = CompositionRoot.BuildServiceProvider();
            viewModel = _services.GetRequiredService<TickerViewModel>();
        }
        catch (Exception ex)
        {
            // A WinExe has no console: without a window the user would see nothing at all.
            Log.Error(ex, "PriceTicker could not start.");
            return CreateStartupErrorWindow(ex);
        }

        // Started on the UI thread, so every refresh resumes there and can update bindings.
        // RunAutoRefreshAsync contains its own failures and completes when cancelled.
        _shutdown = new CancellationTokenSource();
        _ = viewModel.RunAutoRefreshAsync(_shutdown.Token);
        _viewModel = viewModel;

        // The tray icon and its menu bind to the application's data context.
        DataContext = viewModel;
        return new MainWindow { DataContext = viewModel };
    }

    private static Window CreateStartupErrorWindow(Exception exception)
    {
        var details = exception is OptionsValidationException validation
            ? string.Join(Environment.NewLine, validation.Failures)
            : exception.Message;

        return new Window
        {
            Title = "PriceTicker — startup error",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Text = $"PriceTicker could not start. Check appsettings.json and your user secrets.{Environment.NewLine}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}Log: {AppLog.Folder}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>Brings the widget forward whenever the exe is launched again while this copy runs.</summary>
    internal void ShowOnRequestFrom(SingleInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        instance.ShowRequested += (_, _) => Dispatcher.UIThread.Post(ShowMainWindow);
        instance.Listen();
    }

    private void ShowMainWindow()
    {
        if (_desktop?.MainWindow is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();

        // Hidden to the tray, the widget kept running but the user may have been away for hours:
        // showing it again (tray click or launching the exe) fetches fresh prices straight away.
        if (_viewModel is not null && _shutdown is not null)
        {
            _ = _viewModel.RefreshOnShowAsync(_shutdown.Token);
        }
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => ToggleMainWindow();

    private void OnShowHideClicked(object? sender, EventArgs e) => ToggleMainWindow();

    private void OnExitClicked(object? sender, EventArgs e) => _desktop?.Shutdown();

    private void OnOpenLogFolderClicked(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.Folder);
            using var _ = Process.Start(new ProcessStartInfo(AppLog.Folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warning(ex, "Could not open the log folder {Folder}.", AppLog.Folder);
        }
    }

    private void ToggleMainWindow()
    {
        if (_desktop?.MainWindow is not { } window)
        {
            return;
        }

        if (window.IsVisible)
        {
            window.Hide();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // No waiting here: the refresh loop resumes on this (UI) thread, so blocking would deadlock.
        _shutdown?.Cancel();
        _shutdown?.Dispose();
        _services?.Dispose();
    }
}
