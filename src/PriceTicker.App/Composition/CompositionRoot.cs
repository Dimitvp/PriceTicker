using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.App.Configuration;
using PriceTicker.App.Platform;
using PriceTicker.App.ViewModels;
using PriceTicker.Infrastructure.DependencyInjection;
using Serilog;

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
        // Serilog's static logger is set up in Program.Main, before anything else can fail.
        services.AddLogging(logging => logging
            .AddDebug()
            .AddSerilog(dispose: false)
            .SetMinimumLevel(LogLevel.Information));
        services.TryAddSingleton(TimeProvider.System);

        services.AddPriceTickerQuotes(configuration);
        services.AddPriceTickerBullion(configuration);

        services.AddOptions<TickerOptions>()
            .Bind(configuration.GetSection(TickerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TickerOptions>, TickerOptionsValidator>();

        services.AddSingleton(CreateAutoStart);
        services.AddOptions<GoldBarOptions>()
            .Bind(configuration.GetSection(GoldBarOptions.SectionName))
            .Validate(options => options.IsValid, "GoldBar needs a Name, PurchasePrice > 0 and at least one positive ProfitTargetsPercent.")
            .ValidateOnStart();

        services.AddSingleton<GoldBarViewModel>();
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

    private static IAutoStart CreateAutoStart(IServiceProvider services)
    {
        var executablePath = Environment.ProcessPath;

        // Under `dotnet PriceTicker.dll` the process is dotnet itself, which must not be auto-started.
        var isOwnExecutable = executablePath is not null
            && !Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        return OperatingSystem.IsWindows() && isOwnExecutable
            ? new RunKeyAutoStart(new CurrentUserRunKey(), executablePath!, services.GetRequiredService<ILogger<RunKeyAutoStart>>())
            : new UnsupportedAutoStart();
    }

    // Later sources override earlier ones: local file, secrets and environment variables beat appsettings.json.
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            // Personal data (holdings, alert levels, the gold bar): kept out of the public repository.
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<App>(optional: true)
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .Build();
}
