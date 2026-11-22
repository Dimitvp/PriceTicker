using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;
using PriceTicker.App.ViewModels;
using PriceTicker.Infrastructure.DependencyInjection;

namespace PriceTicker.App.Composition;

/// <summary>The single place where the object graph is wired.</summary>
internal static class CompositionRoot
{
    private const string EnvironmentVariablePrefix = "PRICETICKER_";

    /// <summary>
    /// Builds the container and validates every options class that opted in with
    /// <c>ValidateOnStart</c>, so a bad configuration fails here instead of on the first refresh.
    /// </summary>
    /// <exception cref="OptionsValidationException">The configuration is invalid.</exception>
    public static ServiceProvider BuildServiceProvider()
    {
        var configuration = BuildConfiguration();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.TryAddSingleton(TimeProvider.System);

        services.AddPriceTickerQuotes(configuration);

        services.AddOptions<TickerOptions>()
            .Bind(configuration.GetSection(TickerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TickerOptions>, TickerOptionsValidator>();

        services.AddSingleton<TickerViewModel>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        try
        {
            // There is no generic host to run start-up validation, so it is triggered here.
            provider.GetService<IStartupValidator>()?.Validate();
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    // Later sources override earlier ones: secrets and environment variables beat appsettings.json.
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddUserSecrets<App>(optional: true)
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .Build();
}
