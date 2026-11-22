using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PriceTicker.App.Controls;

/// <summary>A small line chart with a soft area fill, sized by layout and coloured by style.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.Gray);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.5);

    private const byte FillAlpha = 40;

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, StrokeThicknessProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Transparent background so the control receives the pointer (and shows its tooltip)
        // even when there is no chart to draw.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var points = SparklineLayout.Map(Values, Bounds.Size, StrokeThickness);
        if (points.Count < 2 || Stroke is not { } stroke)
        {
            return;
        }

        if (stroke is ISolidColorBrush { Color: var color })
        {
            var fill = new SolidColorBrush(Color.FromArgb(FillAlpha, color.R, color.G, color.B));
            context.DrawGeometry(fill, null, CreateArea(points, Bounds.Height));
        }

        var pen = new Pen(stroke, StrokeThickness, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, CreateLine(points));
    }

    private static StreamGeometry CreateLine(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using var figure = geometry.Open();

        figure.BeginFigure(points[0], isFilled: false);
        foreach (var point in points.Skip(1))
        {
            figure.LineTo(point);
        }

        figure.EndFigure(isClosed: false);
        return geometry;
    }

    // The line closed down to the bottom edge, for the soft fill under it.
    private static StreamGeometry CreateArea(IReadOnlyList<Point> points, double bottom)
    {
        var geometry = new StreamGeometry();
        using var figure = geometry.Open();

        figure.BeginFigure(new Point(points[0].X, bottom), isFilled: true);
        foreach (var point in points)
        {
            figure.LineTo(point);
        }

        figure.LineTo(new Point(points[^1].X, bottom));
        figure.EndFigure(isClosed: true);
        return geometry;
    }
}
