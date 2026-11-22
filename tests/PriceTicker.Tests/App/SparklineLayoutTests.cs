using Avalonia;
using PriceTicker.App.Controls;

namespace PriceTicker.Tests.App;

public sealed class SparklineLayoutTests
{
    private static readonly Size Size = new(100, 50);

    [Fact]
    public void Map_SpreadsPointsAcrossWidth_HighestAtTopLowestAtBottom()
    {
        var points = SparklineLayout.Map([10, 30, 20], Size, padding: 0);

        Assert.Equal(new[] { 0d, 50d, 100d }, points.Select(p => p.X));
        Assert.Equal(new[] { 50d, 0d, 25d }, points.Select(p => p.Y));
    }

    [Fact]
    public void Map_KeepsPadding()
    {
        var points = SparklineLayout.Map([1, 2], Size, padding: 5);

        Assert.Equal(new Point(5, 45), points[0]);
        Assert.Equal(new Point(95, 5), points[1]);
    }

    [Fact]
    public void Map_FlatSeries_SitsInTheMiddle()
    {
        var points = SparklineLayout.Map([7, 7, 7], Size, padding: 0);

        Assert.All(points, point => Assert.Equal(25d, point.Y));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Map_TooFewValues_ReturnsNothing(int count)
    {
        Assert.Empty(SparklineLayout.Map([.. Enumerable.Repeat(1d, count)], Size, padding: 0));
    }

    [Fact]
    public void Map_NoRoomAfterPadding_ReturnsNothing()
    {
        Assert.Empty(SparklineLayout.Map([1, 2], new Size(4, 4), padding: 2));
    }
}
