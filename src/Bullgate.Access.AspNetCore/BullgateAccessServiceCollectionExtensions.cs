using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bullgate.Access.AspNetCore;

/// <summary>Registers Bullgate Access services and authentication with dependency injection.</summary>
public static class BullgateAccessServiceCollectionExtensions
{
    /// <summary>Registers the validated typed Access client and adapter cookie services.</summary>
    /// <param name="services">The consumer application's service collection.</param>
    /// <param name="configure">Configures the Access endpoint, credential, timeout, and cookies.</param>
    /// <returns>The same service collection for further registration.</returns>
    public static IServiceCollection AddBullgateAccess(
        this IServiceCollection services,
        Action<BullgateAccessOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<BullgateAccessOptions>()
            .Configure(configure)
            .Validate(
                options => options.BaseAddress?.IsAbsoluteUri == true,
                "Bullgate Access BaseAddress must be an absolute URI.")
            .Validate(
                options => options.IntegrationCredential?.StartsWith(
                    "bgic_",
                    StringComparison.Ordinal) == true,
                "Bullgate Access IntegrationCredential must be a bgic_ credential.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero,
                "Bullgate Access RequestTimeout must be positive.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.CookieName),
                "Bullgate Access CookieName is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FlowCookieName),
                "Bullgate Access FlowCookieName is required.")
            .Validate(
                options => !string.Equals(
                    options.CookieName,
                    options.FlowCookieName,
                    StringComparison.Ordinal),
                "Bullgate Access session and flow cookies must use different names.")
            .ValidateOnStart();

        services.AddHttpClient<IBullgateAccessClient, BullgateAccessClient>(
            static (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<BullgateAccessOptions>>()
                    .Value;
                client.BaseAddress = options.BaseAddress;
                client.Timeout = options.RequestTimeout;
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        options.IntegrationCredential);
            });
        services.AddSingleton<IBullgateSessionCookie, BullgateSessionCookie>();
        services.AddSingleton<IBullgateFlowCookie, BullgateFlowCookie>();

        return services;
    }

    /// <summary>Adds online Bullgate session authentication to an authentication builder.</summary>
    /// <param name="builder">The consumer application's authentication builder.</param>
    /// <returns>The same authentication builder for further scheme registration.</returns>
    public static AuthenticationBuilder AddBullgateSession(
        this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, BullgateSessionAuthenticationHandler>(
            BullgateAccessDefaults.AuthenticationScheme,
            configureOptions: null);
}
