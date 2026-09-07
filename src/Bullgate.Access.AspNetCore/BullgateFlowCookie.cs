using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bullgate.Access.AspNetCore;

/// <summary>Reads and mutates the adapter-owned AccessFlow capability cookie.</summary>
public interface IBullgateFlowCookie
{
    /// <summary>Reads the temporary bearer capability from the current request.</summary>
    /// <param name="httpContext">The current request context.</param>
    /// <returns>The opaque capability, or null when the cookie is absent.</returns>
    string? Read(HttpContext httpContext);

    /// <summary>Writes the capability as an HttpOnly cookie scoped to the Access BFF.</summary>
    /// <param name="httpContext">The current request context.</param>
    /// <param name="capability">The opaque bearer capability issued for the flow.</param>
    /// <param name="expiresAt">The absolute flow expiration.</param>
    void Write(
        HttpContext httpContext,
        string capability,
        DateTimeOffset expiresAt);

    /// <summary>Deletes the capability cookie and marks the response as non-cacheable.</summary>
    /// <param name="httpContext">The current request context.</param>
    void Delete(HttpContext httpContext);
}

internal sealed class BullgateFlowCookie(IOptions<BullgateAccessOptions> options)
    : IBullgateFlowCookie
{
    private readonly BullgateAccessOptions options = options.Value;

    public string? Read(HttpContext httpContext) =>
        httpContext.Request.Cookies.TryGetValue(options.FlowCookieName, out var value)
            ? value
            : null;

    public void Write(
        HttpContext httpContext,
        string capability,
        DateTimeOffset expiresAt)
    {
        httpContext.Response.Cookies.Append(
            options.FlowCookieName,
            capability,
            CreateCookieOptions(httpContext, expiresAt));
        SetNoStore(httpContext.Response);
    }

    public void Delete(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            options.FlowCookieName,
            CreateCookieOptions(httpContext, expiresAt: null));
        SetNoStore(httpContext.Response);
    }

    private CookieOptions CreateCookieOptions(
        HttpContext httpContext,
        DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecurePolicy switch
        {
            CookieSecurePolicy.Always => true,
            CookieSecurePolicy.None => false,
            _ => httpContext.Request.IsHttps,
        },
        SameSite = options.CookieSameSite,
        Path = BullgateAccessDefaults.RoutePrefix,
        Expires = expiresAt,
        IsEssential = true,
    };

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}
