using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceTicker.Core.Bullion;
using PriceTicker.Core.History;
using PriceTicker.Core.Quotes;
using PriceTicker.Infrastructure.Bullion;
using PriceTicker.Infrastructure.TwelveData;
using PriceTicker.Infrastructure.Yahoo;

namespace PriceTicker.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private static readonly ProductInfoHeaderValue UserAgent = new("PriceTicker", "1.0");

    // Real quote payloads are a few KB; anything far larger is a broken or hostile endpoint.
    private const long MaxResponseBytes = 1024 * 1024;

    // Dealer product pages are full HTML pages (about 120–230 KB).
    private const long MaxPageBytes = 2 * 1024 * 1024;

    private const string HttpsRequired = "BaseAddress must be an absolute https URI.";

    /// <summary>
    /// Registers the quote and history services and every built-in provider.
    /// Providers are singletons that create clients through <see cref="IHttpClientFactory"/>,
    /// so long-lived consumers never hold a stale <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddPriceTickerQuotes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<YahooOptions>()
            .Bind(configuration.GetSection(YahooOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsAbsoluteHttps(options.BaseAddress), HttpsRequired)
            .ValidateOnStart();

        services.AddOptions<TwelveDataOptions>()
            .Bind(configuration.GetSection(TwelveDataOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsAbsoluteHttps(options.BaseAddress), HttpsRequired)
            .ValidateOnStart();

        services.AddHttpClient(YahooChartProvider.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<YahooOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        });

        services.AddHttpClient(TwelveDataProvider.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TwelveDataOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        })
        .RedactLoggedHeaders(["Authorization"]);

        // One instance per provider, exposed under each interface it implements.
        services.AddSingleton<YahooChartProvider>();
        services.AddSingleton<TwelveDataProvider>();
        services.AddSingleton<IQuoteProvider>(sp => sp.GetRequiredService<YahooChartProvider>());
        services.AddSingleton<IQuoteProvider>(sp => sp.GetRequiredService<TwelveDataProvider>());
        services.AddSingleton<IPriceHistoryProvider>(sp => sp.GetRequiredService<YahooChartProvider>());
        services.AddSingleton<IPriceHistoryProvider>(sp => sp.GetRequiredService<TwelveDataProvider>());

        services.AddSingleton<IQuoteService, QuoteService>();
        services.AddSingleton<IPriceHistoryService, PriceHistoryService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IBullionService"/> with the Tavex and igold dealers, each read from its
    /// public product page.
    /// </summary>
    public static IServiceCollection AddPriceTickerBullion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<BullionOptions>()
            .Bind(configuration.GetSection(BullionOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsAbsoluteHttps(options.TavexProductUrl) && IsAbsoluteHttps(options.IgoldProductUrl),
                "Dealer product URLs must be absolute https URIs.")
            .ValidateOnStart();

        services.AddHttpClient(DealerPageDealer.HttpClientName, (provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<IOptions<BullionOptions>>().Value.Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            client.MaxResponseContentBufferSize = MaxPageBytes;
        });

        services.AddSingleton<IBullionDealer>(provider =>
            CreateDealer(provider, "Tavex", options => options.TavexProductUrl, TavexPageParser.Parse));
        services.AddSingleton<IBullionDealer>(provider =>
            CreateDealer(provider, "igold", options => options.IgoldProductUrl, IgoldPageParser.Parse));
        services.AddSingleton<IBullionService, BullionService>();

        return services;
    }

    private static DealerPageDealer CreateDealer(
        IServiceProvider provider,
        string name,
        Func<BullionOptions, Uri> productUrl,
        Func<string, DealerPrices?> parse) =>
        new(
            name,
            productUrl(provider.GetRequiredService<IOptions<BullionOptions>>().Value),
            parse,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DealerPageDealer>>());

    private static bool IsAbsoluteHttps(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps;
}
