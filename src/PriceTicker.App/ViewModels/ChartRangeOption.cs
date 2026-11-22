using PriceTicker.Core.History;

namespace PriceTicker.App.ViewModels;

/// <summary>One button of the range selector.</summary>
public sealed record ChartRangeOption(ChartRange Range, string Label);
