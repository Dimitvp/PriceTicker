namespace PriceTicker.Core.Holdings;

/// <summary>Calendar time between two dates in whole years, months and days, e.g. 6 months and 9 days.</summary>
public readonly record struct HoldingPeriod(int Years, int Months, int Days)
{
    /// <summary>
    /// The period from <paramref name="from"/> to <paramref name="to"/>; zero when <paramref name="to"/> is not later.
    /// A month runs to the same day number in the next month (14 May → 14 June).
    /// </summary>
    public static HoldingPeriod Between(DateOnly from, DateOnly to)
    {
        if (to <= from)
        {
            return default;
        }

        var totalMonths = ((to.Year - from.Year) * 12) + to.Month - from.Month;
        if (from.AddMonths(totalMonths) > to)
        {
            totalMonths--;
        }

        var days = to.DayNumber - from.AddMonths(totalMonths).DayNumber;
        return new HoldingPeriod(totalMonths / 12, totalMonths % 12, days);
    }
}
