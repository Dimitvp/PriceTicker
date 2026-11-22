using System.ComponentModel.DataAnnotations;

namespace PriceTicker.Infrastructure.Yahoo;

public sealed class YahooOptions
{
    public const string SectionName = "Providers:Yahoo";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://query1.finance.yahoo.com/");

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
