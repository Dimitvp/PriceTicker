using Avalonia;

namespace PriceTicker.App.Controls;

/// <summary>
/// Pure geometry for <see cref="Sparkline"/>: spreads values evenly across the width and scales
/// them between the lowest (bottom) and highest (top) value. Kept apart from rendering so it can be tested.
/// </summary>
internal static class SparklineLayout
{
    public static IReadOnlyList<Point> Map(IReadOnlyList<double>? values, Size size, double padding)
    {
        if (values is not { Count: >= 2 } || size.Width <= 2 * padding || size.Height <= 2 * padding)
        {
            return [];
        }

        var min = values.Min();
        var max = values.Max();
        var span = max - min;

        var width = size.Width - 2 * padding;
        var height = size.Height - 2 * padding;
        var step = width / (values.Count - 1);

        return [.. values.Select((value, index) => new Point(
            padding + index * step,
            // A flat series sits in the middle instead of dividing by zero.
            span == 0 ? size.Height / 2 : padding + (max - value) / span * height))];
    }

    /// <summary>
    /// The index of the point nearest to horizontal position <paramref name="x"/>, the inverse of
    /// <see cref="Map"/> on the x axis; positions outside the chart snap to the first or last point.
    /// </summary>
    public static int? IndexAt(double x, int count, double width, double padding)
    {
        if (count < 2 || width <= 2 * padding)
        {
            return null;
        }

        var step = (width - 2 * padding) / (count - 1);
        var index = (int)Math.Round((x - padding) / step, MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, count - 1);
    }
}
