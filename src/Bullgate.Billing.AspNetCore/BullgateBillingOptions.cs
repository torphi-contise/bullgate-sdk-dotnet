using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Configures outbound server-to-server communication with Bullgate Billing.</summary>
public sealed class BullgateBillingClientOptions
{
    /// <summary>Gets or sets the absolute Billing base URL, including a trailing slash.</summary>
    public Uri BaseAddress { get; set; } = null!;
    /// <summary>Gets or sets the server-only <c>bgbc_</c> integration credential.</summary>
    public string IntegrationCredential { get; set; } = "";
    /// <summary>Gets or sets the timeout for each Billing request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets whether plain HTTP is accepted for explicit local development.</summary>
    public bool AllowInsecureHttp { get; set; }
}

/// <summary>Configures the inbound credential-authenticated entitlement receiver.</summary>
public sealed class BullgateEntitlementReceiverOptions
{
    /// <summary>Gets or sets the stable environment key included in consumer replay guards.</summary>
    public string EnvironmentKey { get; set; } = "";
    /// <summary>Gets or sets up to four valid <c>bgbd_</c> credentials for rotation.</summary>
    public string[] AcceptedCredentials { get; set; } = [];
}

/// <summary>Registers Bullgate Billing clients and entitlement receiver services.</summary>
public static class BullgateBillingServiceCollectionExtensions
{
    /// <summary>Registers the validated typed Billing client.</summary>
    public static IHttpClientBuilder AddBullgateBillingClient(
        this IServiceCollection services, Action<BullgateBillingClientOptions> configure)
    {
        services.AddOptions<BullgateBillingClientOptions>().Configure(configure)
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true } uri
                && (uri.Scheme == "https" || options.AllowInsecureHttp && uri.Scheme == "http")
                && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && uri.AbsolutePath.EndsWith('/'), "Billing requires an HTTPS base URL ending with '/'.")
            .Validate(options => options.IntegrationCredential is { Length: > 5 }
                && options.IntegrationCredential.StartsWith("bgbc_", StringComparison.Ordinal)
                && !options.IntegrationCredential.Any(char.IsWhiteSpace), "A Billing integration credential is required.")
            .Validate(options => options.RequestTimeout > TimeSpan.Zero
                && options.RequestTimeout <= TimeSpan.FromMinutes(5), "Invalid Billing timeout.")
            .ValidateOnStart();
        return services.AddHttpClient<IBullgateBillingClient, BullgateBillingClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<BullgateBillingClientOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.RequestTimeout;
            // Available offers are paginated (at most 100) and include presentation/store URL metadata.
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.IntegrationCredential);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        });
    }

    /// <summary>
    /// Registers delivery authentication and the consumer-owned entitlement
    /// implementation. The host maps delivery and reversal endpoints separately.
    /// </summary>
    public static IServiceCollection AddBullgateEntitlementReceiver<TApplication>(
        this IServiceCollection services, Action<BullgateEntitlementReceiverOptions> configure)
        where TApplication : class, IBullgateEntitlementApplication
    {
        services.AddOptions<BullgateEntitlementReceiverOptions>().Configure(configure)
            .Validate(options => IsKey(options.EnvironmentKey), "A Billing environment key is required.")
            .Validate(options => options.AcceptedCredentials is { Length: > 0 and <= 4 } credentials
                && credentials.All(BullgateDeliveryCredential.IsValid), "Configure independent bgbd_ delivery credentials.")
            .ValidateOnStart();
        services.AddScoped<IBullgateEntitlementApplication, TApplication>();
        services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
            BullgateDeliveryAuthenticationHandler>(BullgateDeliveryAuthenticationHandler.SchemeName, null);
        services.AddAuthorization();
        return services;
    }

    internal static bool IsKey(string? key) => key is { Length: > 0 and <= 100 }
        && char.IsAsciiLetterLower(key[0])
        && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.');
}
