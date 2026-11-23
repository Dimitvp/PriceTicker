using System.Text;
using Microsoft.Extensions.Configuration;
using PriceTicker.App.Configuration;

namespace PriceTicker.Tests.App;

public sealed class HoldingOptionsBindingTests
{
    [Fact]
    public void FirstPurchase_BindsFromAnIsoDateInJson()
    {
        const string json = """{ "Holdings": { "sxr8": { "Quantity": 2.5, "Invested": 1000, "FirstPurchase": "2030-03-10" } } }""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var options = new TickerOptions();
        configuration.Bind(options);

        Assert.Equal(new DateOnly(2030, 3, 10), options.Holdings["sxr8"].ToHolding().Position!.FirstPurchase);
    }
}
