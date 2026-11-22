using System.ComponentModel.DataAnnotations;

namespace PriceTicker.Infrastructure.TwelveData;

public sealed class TwelveDataOptions
{
    public const string SectionName = "Providers:TwelveData";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://api.twelvedata.com/");

    /// <summary>
    /// Free key from twelvedata.com. Never commit it: supply it through user secrets or the
    /// <c>PRICETICKER_Providers__TwelveData__ApiKey</c> environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    public override string ToString() => $"TwelveDataOptions {{ BaseAddress = {BaseAddress}, ApiKey = ***, Timeout = {Timeout} }}";
}
