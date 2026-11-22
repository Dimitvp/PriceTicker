using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PriceTicker.Core.Quotes;
using PriceTicker.Infrastructure.TwelveData;
using PriceTicker.Infrastructure.Yahoo;

namespace PriceTicker.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private static readonly ProductInfoHeaderValue UserAgent = new("PriceTicker", "1.0");

    // Real quote payloads are a few KB; anything far larger is a broken or hostile endpoint.
    private const long MaxResponseBytes = 1024 * 1024;

    private const string HttpsRequired = "BaseAddress must be an absolute https URI.";

    /// <summary>
    /// Registers <see cref="IQuoteService"/> and every built-in <see cref="IQuoteProvider"/>.
    /// Providers are singletons that create clients through <see cref="IHttpClientFactory"/>,
    /// so long-lived consumers never hold a stale <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddPriceTickerQuotes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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

        services.AddHttpClient(YahooChartQuoteProvider.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<YahooOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        });

        services.AddHttpClient(TwelveDataQuoteProvider.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TwelveDataOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        })
        .RedactLoggedHeaders(["Authorization"]);

        services.AddSingleton<IQuoteProvider, YahooChartQuoteProvider>();
        services.AddSingleton<IQuoteProvider, TwelveDataQuoteProvider>();
        services.AddSingleton<IQuoteService, QuoteService>();

        return services;
    }

    private static bool IsAbsoluteHttps(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps;
}
