using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceTicker.Core.Instruments;
using PriceTicker.Core.Quotes;
using PriceTicker.Infrastructure.Tradegate;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class TradegateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 8, 50, 0, TimeSpan.Zero);

    // Shape seen from refresh.php: mostly numbers, now and then a German-formatted string.
    private const string Smh = """
        { "bid": 72.01, "ask": 72.22, "bidsize": 500, "asksize": 500, "delta": 1.65, "stueck": 7674,
          "umsatz": 553798, "avg": 72.1655, "executions": 246, "last": 72.04, "high": 72.49,
          "low": "71,50", "indication": 1, "close": 70.87 }
        """;

    private static readonly Instrument SmhInstrument = new("smh", "SMH", "SMH.MI", "Yahoo", ParseIsin("IE00BMC38736"));

    [Fact]
    public void Parse_TakesLastPriceAndTradegateClose()
    {
        var quote = Available(TradegateQuoteParser.Parse(Json(Smh), SmhInstrument, Now));

        Assert.Equal(72.04m, quote.Price);
        Assert.Equal(70.87m, quote.PreviousClose);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal("Tradegate", quote.Market!.Exchange);
    }

    [Theory]
    [InlineData("\"71,50\"", 71.50)]
    [InlineData("\"1.234,56\"", 1234.56)]
    [InlineData("\"709.38\"", 709.38)]
    [InlineData("709.38", 709.38)]
    public void TryRead_ReadsNumbersAndGermanStrings(string json, double expected)
    {
        Assert.True(TradegateQuoteParser.TryRead(Json($$"""{ "x": {{json}} }"""), "x", out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void Parse_WithoutTrades_UsesTheMidPoint()
    {
        var quote = Available(TradegateQuoteParser.Parse(Json("""{ "bid": 10, "ask": 12, "last": 0 }"""), SmhInstrument, Now));

        Assert.Equal(11m, quote.Price);
    }

    [Fact]
    public void Parse_WithNoPriceAtAll_IsUnavailable()
    {
        Assert.IsType<QuoteUnavailable>(TradegateQuoteParser.Parse(Json("""{ "bid": 0, "ask": 0 }"""), SmhInstrument, Now));
    }

    [Fact]
    public async Task GetQuotesAsync_AsksByIsin_AndSkipsInstrumentsWithout()
    {
        var handler = StubHttpMessageHandler.Json(Smh);
        var service = CreateService(handler);
        var bitcoin = TestInstruments.Create("btc");

        var results = await service.GetQuotesAsync([SmhInstrument, bitcoin], CancellationToken.None);

        Assert.IsType<QuoteAvailable>(results[0]);
        Assert.IsType<QuoteUnavailable>(results[1]);
        Assert.Equal("isin=IE00BMC38736", Assert.Single(handler.Requests).RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetQuotesAsync_WhenTheBodyIsNotJson_ReportsNotTraded()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") });
        var service = CreateService(handler);

        var result = Assert.Single(await service.GetQuotesAsync([SmhInstrument], CancellationToken.None));

        Assert.Equal("Not traded on Tradegate.", Assert.IsType<QuoteUnavailable>(result).Reason);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenSwitchedOff_SendsNothing()
    {
        var handler = StubHttpMessageHandler.Json(Smh);
        var service = CreateService(handler, enabled: false);

        Assert.IsType<QuoteUnavailable>(Assert.Single(await service.GetQuotesAsync([SmhInstrument], CancellationToken.None)));
        Assert.Empty(handler.Requests);
    }

    private static TradegateQuoteService CreateService(StubHttpMessageHandler handler, bool enabled = true) =>
        new(
            new StubHttpClientFactory(handler, new Uri("https://tradegate.test/")),
            Options.Create(new TradegateOptions { Enabled = enabled }),
            TimeProvider.System,
            NullLogger<TradegateQuoteService>.Instance);

    private static Quote Available(QuoteResult result) => Assert.IsType<QuoteAvailable>(result).Quote;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Isin ParseIsin(string text) =>
        Isin.TryParse(text, out var isin) ? isin : throw new ArgumentException("Invalid ISIN.", nameof(text));
}
