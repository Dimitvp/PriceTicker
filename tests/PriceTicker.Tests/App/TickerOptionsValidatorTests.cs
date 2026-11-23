using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;

namespace PriceTicker.Tests.App;

public sealed class TickerOptionsValidatorTests
{
    private readonly TickerOptionsValidator _validator = new();

    [Fact]
    public void Validate_WithValidOptions_Succeeds()
    {
        var result = _validator.Validate(null, Options(TimeSpan.FromMinutes(5), Instrument("a"), Instrument("b")));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithIntervalAtMinimum_Succeeds()
    {
        var result = _validator.Validate(null, Options(TickerOptionsValidator.MinimumRefreshInterval, Instrument("a")));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithTooShortInterval_Fails()
    {
        var result = _validator.Validate(null, Options(TimeSpan.FromSeconds(10), Instrument("a")));

        AssertFailsWith(result, "RefreshInterval");
    }

    [Fact]
    public void Validate_WithNoInstruments_Fails()
    {
        var result = _validator.Validate(null, Options(TimeSpan.FromMinutes(5)));

        AssertFailsWith(result, "at least one instrument");
    }

    [Fact]
    public void Validate_WithDuplicateIds_Fails()
    {
        var result = _validator.Validate(null, Options(TimeSpan.FromMinutes(5), Instrument("a"), Instrument("a")));

        AssertFailsWith(result, "duplicate Id 'a'");
    }

    [Fact]
    public void Validate_WithBlankField_Fails()
    {
        var blank = Instrument("a");
        blank.Symbol = " ";

        var result = _validator.Validate(null, Options(TimeSpan.FromMinutes(5), blank));

        AssertFailsWith(result, "Instruments:0");
    }

    [Fact]
    public void Validate_WithHoldingForUnknownInstrument_Fails()
    {
        var options = Options(TimeSpan.FromMinutes(5), Instrument("a"));
        options.Holdings["typo"] = new HoldingOptions { PurchasePrice = 1m };

        AssertFailsWith(_validator.Validate(null, options), "Ticker:Holdings:typo does not match");
    }

    [Fact]
    public void Validate_WithNonPositivePurchasePrice_Fails()
    {
        var options = Options(TimeSpan.FromMinutes(5), Instrument("a"));
        options.Holdings["a"] = new HoldingOptions { PurchasePrice = 0m };

        AssertFailsWith(_validator.Validate(null, options), "PurchasePrice must be greater than zero");
    }

    private static void AssertFailsWith(ValidateOptionsResult result, string expectedFragment)
    {
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains(expectedFragment, StringComparison.Ordinal));
    }

    private static TickerOptions Options(TimeSpan interval, params InstrumentOptions[] instruments) =>
        new() { RefreshInterval = interval, Instruments = [.. instruments] };

    private static InstrumentOptions Instrument(string id) =>
        new() { Id = id, DisplayName = id, Symbol = id, Provider = "Yahoo" };
}
