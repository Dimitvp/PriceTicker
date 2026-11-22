namespace PriceTicker.Core.Instruments;

/// <summary>Base of every per-instrument outcome (a quote, a price history).</summary>
public abstract record InstrumentResult
{
    private protected InstrumentResult(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        Instrument = instrument;
    }

    public Instrument Instrument { get; }
}
