using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bullgate.Access.AspNetCore;

/// <summary>Reads and mutates the adapter-owned host-only session cookie.</summary>
public interface IBullgateSessionCookie
{
    /// <summary>Reads the opaque session token from the current request.</summary>
    /// <param name="httpContext">The current request context.</param>
    /// <returns>The opaque token, or null when the cookie is absent.</returns>
    string? Read(HttpContext httpContext);

    /// <summary>Writes the opaque token as an HttpOnly cookie until its Access expiration.</summary>
    /// <param name="httpContext">The current request context.</param>
    /// <param name="sessionToken">The opaque bearer token issued by Access.</param>
    /// <param name="expiresAt">The absolute Access session expiration.</param>
    void Write(
        HttpContext httpContext,
        string sessionToken,
        DateTimeOffset expiresAt);

    /// <summary>Deletes the session cookie and marks the response as non-cacheable.</summary>
    /// <param name="httpContext">The current request context.</param>
    void Delete(HttpContext httpContext);
}

internal sealed class BullgateSessionCookie(IOptions<BullgateAccessOptions> options)
    : IBullgateSessionCookie
{
    private readonly BullgateAccessOptions options = options.Value;

    public string? Read(HttpContext httpContext) =>
        httpContext.Request.Cookies.TryGetValue(options.CookieName, out var value)
            ? value
            : null;

    public void Write(
        HttpContext httpContext,
        string sessionToken,
        DateTimeOffset expiresAt)
    {
        httpContext.Response.Cookies.Append(
            options.CookieName,
            sessionToken,
            CreateCookieOptions(httpContext, expiresAt));
        SetNoStore(httpContext.Response);
    }

    public void Delete(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            options.CookieName,
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
        Path = "/",
        Expires = expiresAt,
        IsEssential = true,
    };

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}
