using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PriceTicker.Core.Bullion;
using PriceTicker.Infrastructure.Bullion;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Infrastructure;

public sealed class DealerPageTests
{
    // Excerpts of the real product pages, read on 23.11.2026.
    private const string TavexPage = """
        <span class="product-poster__price-value js-product-price-from" data-type="sell" data-isPackage="0" data-pricelist='{"buy":[{"price":1103,"vatRate":null,"vat":null,"noVatPrice":null,"rate":null,"quantityFrom":1,"quantityTo":0}],"sell":[{"price":1133,"vatRate":0,"vat":0,"noVatPrice":1133,"rate":null,"quantityFrom":1,"quantityTo":0}]}'> 1133,00 € </span>
        <div class="item-list__label">Цена при обратно изкупуване </div> <div class="item-list__data">1103,00 €</div>
        """;

    private const string IgoldPage = """
        <td class="pl-3.5"><span class="productUpdatePriceSell">1130.34 €</span></td>
        <td class="pl-3.5"><span class="productUpdatePriceBuy">1104.64 €</span></td>
        <span class="productUpdatePriceSell-44">5 590.00 €</span>
        """;

    [Fact]
    public void Tavex_ReadsSellAsBuyPrice_AndBuyAsBuyback()
    {
        Assert.Equal(new DealerPrices(1133m, 1103m), TavexPageParser.Parse(TavexPage));
    }

    [Fact]
    public void Tavex_IgnoresRelatedProductPriceListsBeforeTheMainOne()
    {
        const string page = """
            <div class="related" data-pricelist='{"buy":[{"price":53,"quantityFrom":1}],"sell":[{"price":60,"quantityFrom":1}]}'></div>
            <span class="product-poster__price-value js-product-price-from" data-type="sell" data-pricelist='{"buy":[{"price":1103,"quantityFrom":1}],"sell":[{"price":1133,"quantityFrom":1}]}'>1133,00 €</span>
            """;

        Assert.Equal(new DealerPrices(1133m, 1103m), TavexPageParser.Parse(page));
    }

    [Fact]
    public void Tavex_PicksTheSinglePieceTier_AndDecodesEntities()
    {
        const string page = """
            <span class="js-product-price-from" data-pricelist='{&quot;buy&quot;:[{&quot;price&quot;:1090,&quot;quantityFrom&quot;:10},{&quot;price&quot;:1103,&quot;quantityFrom&quot;:1}],&quot;sell&quot;:[{&quot;price&quot;:1133,&quot;quantityFrom&quot;:1}]}'>
            """;

        Assert.Equal(new DealerPrices(1133m, 1103m), TavexPageParser.Parse(page));
    }

    [Theory]
    [InlineData("<html>redesigned page</html>")]
    [InlineData("data-pricelist='not json'")]
    [InlineData("""data-pricelist='{"buy":[],"sell":[{"price":1133}]}'""")]
    public void Tavex_WithoutPriceList_ReturnsNull(string html)
    {
        Assert.Null(TavexPageParser.Parse(html));
    }

    [Fact]
    public void Igold_ReadsMainProductPrices_IgnoringRelatedProducts()
    {
        Assert.Equal(new DealerPrices(1130.34m, 1104.64m), IgoldPageParser.Parse(IgoldPage));
    }

    [Fact]
    public void Igold_WithOnlyOneOfThePrices_ReturnsNull()
    {
        Assert.Null(IgoldPageParser.Parse("""<span class="productUpdatePriceSell">1130.34 €</span>"""));
    }

    [Theory]
    [InlineData("1130.34 €", 1130.34)]
    [InlineData("1 130,34 €", 1130.34)]
    [InlineData("1,130.34", 1130.34)]
    [InlineData("1.130,34", 1130.34)]
    [InlineData("1133,00 €", 1133)]
    [InlineData("1 133 €", 1133)]
    [InlineData("1&nbsp;130.34&nbsp;€", 1130.34)]
    [InlineData("1.130", 1130)]
    [InlineData("1,130", 1130)]
    [InlineData("1130.34 € / 2210.70 лв.", 1130.34)]
    [InlineData("1 130,34 € (2 210,70 лв.)", 1130.34)]
    public void PriceText_ReadsCommonFormats(string text, decimal expected)
    {
        Assert.True(PriceText.TryParse(text, out var price));
        Assert.Equal(expected, price);
    }

    [Theory]
    [InlineData("")]
    [InlineData("€")]
    [InlineData("0.00 €")]
    public void PriceText_RejectsNonPrices(string text)
    {
        Assert.False(PriceText.TryParse(text, out _));
    }

    [Fact]
    public async Task Dealer_ReadsPricesFromItsPage()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 7, 0, 0, TimeSpan.Zero));
        var dealer = CreateDealer(StubHttpMessageHandler.Json(IgoldPage), IgoldPageParser.Parse, clock);

        var result = await dealer.GetQuoteAsync(CancellationToken.None);

        var quote = Assert.IsType<BullionQuoteAvailable>(result).Quote;
        Assert.Equal(("igold", 1130.34m, 1104.64m), (quote.Dealer, quote.BuyPrice, quote.BuybackPrice));
        Assert.Equal(clock.GetUtcNow(), quote.AsOf);
    }

    [Theory]
    [InlineData(1103, 1133)]
    [InlineData(1133, 900)]
    public async Task Dealer_WithImplausiblePrices_RefusesThem(decimal buy, decimal buyback)
    {
        var page = $"""<span class="productUpdatePriceSell">{buy} €</span><span class="productUpdatePriceBuy">{buyback} €</span>""";
        var dealer = CreateDealer(StubHttpMessageHandler.Json(page), IgoldPageParser.Parse);

        var result = await dealer.GetQuoteAsync(CancellationToken.None);

        Assert.Equal("Prices look wrong.", Assert.IsType<BullionQuoteUnavailable>(result).Reason);
    }

    [Fact]
    public async Task Dealer_WhenRequestTimesOut_ReportsTimedOut()
    {
        var dealer = CreateDealer(StubHttpMessageHandler.Throws(new TaskCanceledException("timeout")), IgoldPageParser.Parse);

        var result = await dealer.GetQuoteAsync(CancellationToken.None);

        Assert.Equal("Timed out.", Assert.IsType<BullionQuoteUnavailable>(result).Reason);
    }

    [Fact]
    public async Task Dealer_WhenCallerCancels_Throws()
    {
        var dealer = CreateDealer(StubHttpMessageHandler.Throws(new TaskCanceledException()), IgoldPageParser.Parse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dealer.GetQuoteAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Dealer_WhenPageChanged_SaysPricesNotFound()
    {
        var dealer = CreateDealer(StubHttpMessageHandler.Json("<html></html>"), IgoldPageParser.Parse);

        var result = await dealer.GetQuoteAsync(CancellationToken.None);

        Assert.Equal("Prices not found on page.", Assert.IsType<BullionQuoteUnavailable>(result).Reason);
    }

    [Fact]
    public async Task Dealer_WhenBlocked_ReportsStatusCode()
    {
        var dealer = CreateDealer(StubHttpMessageHandler.Json("", HttpStatusCode.Forbidden), IgoldPageParser.Parse);

        var result = await dealer.GetQuoteAsync(CancellationToken.None);

        Assert.Equal("HTTP 403", Assert.IsType<BullionQuoteUnavailable>(result).Reason);
    }

    [Fact]
    public async Task Dealer_WhenOffline_ReportsNetworkError()
    {
        var dealer = CreateDealer(StubHttpMessageHandler.Throws(new HttpRequestException("offline")), IgoldPageParser.Parse);

        var result = await dealer.GetQuoteAsync(CancellationToken.None);

        Assert.Equal("Network error.", Assert.IsType<BullionQuoteUnavailable>(result).Reason);
    }

    private static DealerPageDealer CreateDealer(
        StubHttpMessageHandler handler,
        Func<string, DealerPrices?> parse,
        TimeProvider? clock = null) =>
        new(
            "igold",
            new Uri("https://dealer.test/product"),
            parse,
            new StubHttpClientFactory(handler, new Uri("https://dealer.test/")),
            clock ?? new FakeTimeProvider(),
            NullLogger<DealerPageDealer>.Instance);
}
