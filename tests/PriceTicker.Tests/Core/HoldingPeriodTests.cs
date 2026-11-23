using PriceTicker.Core.Holdings;

namespace PriceTicker.Tests.Core;

public sealed class HoldingPeriodTests
{
    [Theory]
    [InlineData("2030-03-10", "2030-09-19", 0, 6, 9)]
    [InlineData("2030-04-02", "2030-08-19", 0, 4, 17)]
    [InlineData("2030-09-19", "2030-09-19", 0, 0, 0)]
    [InlineData("2030-09-16", "2030-09-19", 0, 0, 3)]
    [InlineData("2030-03-10", "2030-04-09", 0, 0, 30)]
    [InlineData("2030-03-10", "2030-04-10", 0, 1, 0)]
    [InlineData("2030-03-10", "2032-05-16", 2, 2, 6)]
    [InlineData("2030-01-31", "2030-03-01", 0, 1, 1)]
    [InlineData("2030-09-20", "2030-09-19", 0, 0, 0)]
    public void Between_CountsWholeCalendarMonthsThenDays(string from, string to, int years, int months, int days)
    {
        var period = HoldingPeriod.Between(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(new HoldingPeriod(years, months, days), period);
    }

    [Fact]
    public void Position_WithoutFirstPurchase_HasNoHoldingPeriod()
    {
        Assert.Null(new Position(1m, 100m).HeldFor(new DateOnly(2030, 9, 19)));
    }
}
