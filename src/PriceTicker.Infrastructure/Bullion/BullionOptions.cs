using System.ComponentModel.DataAnnotations;

namespace PriceTicker.Infrastructure.Bullion;

/// <summary>The <c>Providers:Bullion</c> section: the product pages read for each dealer.</summary>
public sealed class BullionOptions
{
    public const string SectionName = "Providers:Bullion";

    [Required]
    public Uri TavexProductUrl { get; set; } = new("https://tavex.bg/zlato/10-grama-zlatno-kulche-valcambi/");

    [Required]
    public Uri IgoldProductUrl { get; set; } = new("https://igold.bg/zlatno-kyulche-10-g-valcambi");

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}
