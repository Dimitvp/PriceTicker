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
}
