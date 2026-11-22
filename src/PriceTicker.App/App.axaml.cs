using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PriceTicker.App.Composition;
using PriceTicker.App.ViewModels;
using PriceTicker.App.Views;

namespace PriceTicker.App;

[SuppressMessage("Design", "CA1001", Justification = "Avalonia owns the Application lifetime; fields are disposed in OnExit.")]
public sealed partial class App : Application
{
    private ServiceProvider? _services;
    private CancellationTokenSource? _shutdown;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

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
            return CreateStartupErrorWindow(ex);
        }

        // Started on the UI thread, so every refresh resumes there and can update bindings.
        // RunAutoRefreshAsync contains its own failures and completes when cancelled.
        _shutdown = new CancellationTokenSource();
        _ = viewModel.RunAutoRefreshAsync(_shutdown.Token);

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
                Text = $"PriceTicker could not start. Check appsettings.json and your user secrets.{Environment.NewLine}{Environment.NewLine}{details}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => ToggleMainWindow();

    private void OnShowHideClicked(object? sender, EventArgs e) => ToggleMainWindow();

    private void OnExitClicked(object? sender, EventArgs e) => _desktop?.Shutdown();

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
            window.Show();
            window.Activate();
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
