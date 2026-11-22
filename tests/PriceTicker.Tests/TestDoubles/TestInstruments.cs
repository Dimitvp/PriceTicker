using PriceTicker.Core.Instruments;

namespace PriceTicker.Tests.TestDoubles;

internal static class TestInstruments
{
    public static Instrument Create(string id, string provider = "Fake", string? symbol = null) =>
        new(id, $"Name {id}", symbol ?? id.ToUpperInvariant(), provider);
}
