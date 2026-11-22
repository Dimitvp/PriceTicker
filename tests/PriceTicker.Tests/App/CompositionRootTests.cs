using Microsoft.Extensions.DependencyInjection;
using PriceTicker.App.Composition;
using PriceTicker.App.ViewModels;
using PriceTicker.Core.Quotes;

namespace PriceTicker.Tests.App;

/// <summary>
/// Smoke test of the real wiring and the shipped appsettings.json (copied to the test output
/// from the App project). Catches DI and options mistakes no unit test sees. No network use.
/// </summary>
public sealed class CompositionRootTests
{
    [Fact]
    public void BuildServiceProvider_WithShippedConfiguration_ResolvesTheViewModel()
    {
        using var services = CompositionRoot.BuildServiceProvider();

        var viewModel = services.GetRequiredService<TickerViewModel>();

        Assert.NotEmpty(viewModel.Items);
        Assert.Equal(2, services.GetServices<IQuoteProvider>().Count());
    }
}
