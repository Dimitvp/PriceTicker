namespace PriceTicker.Core.Instruments;

/// <summary>A data source that instruments select by name through <see cref="Instrument.ProviderKey"/>.</summary>
public interface IKeyedProvider
{
    string Key { get; }
}
