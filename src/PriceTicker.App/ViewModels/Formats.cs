using System.Globalization;
using PriceTicker.Core.Holdings;

namespace PriceTicker.App.ViewModels;

/// <summary>The few display formats shared by the view models, in the current culture.</summary>
internal static class Formats
{
    /// <summary>"1,234.50" — market prices.</summary>
    public static string Price(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>"4.125" or "100.00" — a price someone typed in, shown without losing digits.</summary>
    public static string ExactPrice(decimal value) => value.ToString("#,##0.00##", CultureInfo.CurrentCulture);

    /// <summary>"1,110.00 EUR".</summary>
    public static string Euro(decimal amount) => $"{Price(amount)} EUR";

    /// <summary>"+2.88" / "-10.00" / "0.00".</summary>
    public static string Signed(decimal value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);

    /// <summary>"10:00" when <paramref name="time"/> is on the same day as <paramref name="now"/>, otherwise "Mon 10:00" — local time.</summary>
    public static string ClockTime(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        var format = local.Date == now.ToLocalTime().Date ? "HH:mm" : "ddd HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>"Wed" — the day of a past session, in local time.</summary>
    public static string Weekday(DateTimeOffset time) => time.ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);

    /// <summary>"10.03.2030".</summary>
    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>The two largest units: "1 y 2 mo", "6 mo 9 d", "17 d".</summary>
    public static string Period(HoldingPeriod period) => period switch
    {
        { Years: > 0 } => $"{period.Years} y {period.Months} mo",
        { Months: > 0 } => $"{period.Months} mo {period.Days} d",
        _ => $"{period.Days} d",
    };

    /// <summary>"22.11.2026 19:27" in local time.</summary>
    public static string Timestamp(DateTimeOffset time) =>
        time.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
}
