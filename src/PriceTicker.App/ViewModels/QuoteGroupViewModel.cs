namespace PriceTicker.App.ViewModels;

/// <summary>A headed section of rows, e.g. the long-term core apart from the thematic bets.</summary>
public sealed class QuoteGroupViewModel
{
    public QuoteGroupViewModel(string? title, string? note, IReadOnlyList<QuoteItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Title = title?.Trim() ?? string.Empty;
        Note = note?.Trim() ?? string.Empty;
        Items = items;
    }

    /// <summary>Empty for the trailing rows that belong to no group.</summary>
    public string Title { get; }

    public string Note { get; }

    public bool HasHeading => Title.Length > 0;

    public bool HasNote => Note.Length > 0;

    public IReadOnlyList<QuoteItemViewModel> Items { get; }
}
