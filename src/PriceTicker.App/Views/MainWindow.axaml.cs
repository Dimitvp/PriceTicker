using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PriceTicker.App.Views;

/// <summary>Borderless widget window. Code-behind holds window chrome behaviour only.</summary>
public sealed partial class MainWindow : Window
{
    private const int ScreenMargin = 24;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += KeepRightEdgeInPlace;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceInTopRightCorner();
    }

    private void PlaceInTopRightCorner()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var widthInPixels = (int)Math.Ceiling(Bounds.Width * screen.Scaling);
        Position = new PixelPoint(area.Right - widthInPixels - ScreenMargin, area.Y + ScreenMargin);
    }

    // The window sizes to its content, so it widens once prices load. Growing leftwards keeps
    // it anchored where it was placed or dragged, instead of running off the right edge.
    private void KeepRightEdgeInPlace(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || e.PreviousSize.Width <= 0)
        {
            return;
        }

        var growthInPixels = (int)Math.Round((e.NewSize.Width - e.PreviousSize.Width) * DesktopScaling);
        Position = new PixelPoint(Position.X - growthInPixels, Position.Y);
    }

    private void OnDragAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();
}
