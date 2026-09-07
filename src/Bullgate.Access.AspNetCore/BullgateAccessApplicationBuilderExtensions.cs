using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bullgate.Access.AspNetCore;

/// <summary>Adds Bullgate Access middleware to the ASP.NET Core request pipeline.</summary>
public static class BullgateAccessApplicationBuilderExtensions
{
    /// <summary>
    /// Maps Access transport and invalid-response failures to a no-store HTTP 503
    /// response while preserving the session cookie.
    /// </summary>
    /// <param name="application">The consumer application's request pipeline.</param>
    /// <returns>The same builder for further pipeline configuration.</returns>
    public static IApplicationBuilder UseBullgateAccessFailures(
        this IApplicationBuilder application) =>
        application.UseMiddleware<BullgateAccessFailureMiddleware>();
}

internal sealed class BullgateAccessFailureMiddleware(
    RequestDelegate next,
    ILogger<BullgateAccessFailureMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BullgateAccessUnavailableException exception)
            when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "Bullgate Access is unavailable.");
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                new AccessErrorResponse("access-unavailable"),
                JsonOptions,
                context.RequestAborted);
        }
    }

    private sealed record AccessErrorResponse(string Error);
}
