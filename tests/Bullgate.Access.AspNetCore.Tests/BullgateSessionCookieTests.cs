using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bullgate.Access.AspNetCore.Tests;

public sealed class BullgateSessionCookieTests
{
    [Fact]
    public void Write_UsesAHostOnlyHttpOnlyCookieAndNoStore()
    {
        var context = new DefaultHttpContext();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var cookie = CreateCookie();

        cookie.Write(context, "bgs_secret", expiresAt);

        var header = Assert.Single(context.Response.Headers.SetCookie);
        Assert.Contains("bullgate.session=bgs_secret", header, StringComparison.Ordinal);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public void Delete_UsesTheSameHostOnlyCookieScope()
    {
        var context = new DefaultHttpContext();
        var cookie = CreateCookie();

        cookie.Delete(context);

        var header = Assert.Single(context.Response.Headers.SetCookie);
        Assert.Contains("bullgate.session=", header, StringComparison.Ordinal);
        Assert.Contains("expires=", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FlowCookie_IsHttpOnlyAndScopedToTheAdapterRoutes()
    {
        var context = new DefaultHttpContext();
        var cookie = new BullgateFlowCookie(
            Options.Create(new BullgateAccessOptions
            {
                BaseAddress = new Uri("https://access.example.test"),
                IntegrationCredential = "bgic_test.secret",
            }));

        cookie.Write(context, "bgf_opaque", DateTimeOffset.UtcNow.AddMinutes(30));

        var header = Assert.Single(context.Response.Headers.SetCookie);
        Assert.Contains("bullgate.flow=bgf_opaque", header, StringComparison.Ordinal);
        Assert.Contains(
            $"path={BullgateAccessDefaults.RoutePrefix}",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
    }

    private static BullgateSessionCookie CreateCookie() =>
        new BullgateSessionCookie(
            Options.Create(new BullgateAccessOptions
            {
                BaseAddress = new Uri("https://access.example.test"),
                IntegrationCredential = "bgic_test.secret",
            }));
}
