using System.ComponentModel.DataAnnotations;

namespace PriceTicker.Infrastructure.Tradegate;

public sealed class TradegateOptions
{
    public const string SectionName = "Providers:Tradegate";

    /// <summary>Off: no Tradegate requests and no extended-hours line in the widget.</summary>
    public bool Enabled { get; set; } = true;

    [Required]
    public Uri BaseAddress { get; set; } = new("https://www.tradegatebsx.com/");

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
