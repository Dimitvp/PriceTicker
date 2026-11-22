using Microsoft.Extensions.Logging.Abstractions;
using PriceTicker.Core.Quotes;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.Core;

public sealed class QuoteServiceTests
{
    [Fact]
    public async Task GetQuotesAsync_RoutesEachInstrumentToItsProvider_AndKeepsCallerOrder()
    {
        var alpha = FakeQuoteProvider.PricingEverythingAt("Alpha", 1m);
        var beta = FakeQuoteProvider.PricingEverythingAt("Beta", 2m);
        var service = CreateService(alpha, beta);
        var instruments = new[]
        {
            TestInstruments.Create("a1", "Alpha"),
            TestInstruments.Create("b1", "Beta"),
            TestInstruments.Create("a2", "Alpha"),
        };

        var results = await service.GetQuotesAsync(instruments, CancellationToken.None);

        Assert.Equal(new[] { "a1", "b1", "a2" }, results.Select(r => r.Instrument.Id));
        Assert.Equal(new[] { 1m, 2m, 1m }, results.Cast<QuoteAvailable>().Select(r => r.Quote.Price));
        Assert.Single(alpha.Calls);
        Assert.Single(beta.Calls);
    }

    [Fact]
    public async Task GetQuotesAsync_WithUnknownProvider_ReportsUnavailable()
    {
        var service = CreateService(FakeQuoteProvider.PricingEverythingAt("Alpha", 1m));

        var results = await service.GetQuotesAsync([TestInstruments.Create("x", "Missing")], CancellationToken.None);

        var unavailable = Assert.IsType<QuoteUnavailable>(Assert.Single(results));
        Assert.Contains("Missing", unavailable.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenOneProviderThrows_OtherProvidersStillAnswer()
    {
        var service = CreateService(
            FakeQuoteProvider.Throwing("Broken", new InvalidOperationException("boom")),
            FakeQuoteProvider.PricingEverythingAt("Healthy", 5m));

        var results = await service.GetQuotesAsync(
            [TestInstruments.Create("bad", "Broken"), TestInstruments.Create("good", "Healthy")],
            CancellationToken.None);

        Assert.IsType<QuoteUnavailable>(results[0]);
        Assert.Equal(5m, Assert.IsType<QuoteAvailable>(results[1]).Quote.Price);
    }

    [Fact]
    public async Task GetQuotesAsync_WhenProviderOmitsAnInstrument_ReportsItUnavailable()
    {
        var silent = new FakeQuoteProvider("Alpha", _ => []);
        var service = CreateService(silent);

        var results = await service.GetQuotesAsync([TestInstruments.Create("a", "Alpha")], CancellationToken.None);

        Assert.IsType<QuoteUnavailable>(Assert.Single(results));
    }

    [Fact]
    public async Task GetQuotesAsync_WhenCancelled_Throws()
    {
        var service = CreateService(FakeQuoteProvider.PricingEverythingAt("Alpha", 1m));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetQuotesAsync([TestInstruments.Create("a", "Alpha")], new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Constructor_WithDuplicateProviderKeys_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateService(
            FakeQuoteProvider.PricingEverythingAt("Same", 1m),
            FakeQuoteProvider.PricingEverythingAt("same", 2m)));
    }

    private static QuoteService CreateService(params IQuoteProvider[] providers) =>
        new(providers, NullLogger<QuoteService>.Instance);
}
