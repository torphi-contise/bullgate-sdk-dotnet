using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Bullgate.Billing.AspNetCore;

internal static class BullgateBillingBff
{
    internal static async Task<IResult> ExecuteAsync(HttpContext context,
        Func<HttpContext, string?> resolveCustomerReference, string? invalidField, Func<string, Task<IResult>> operation)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (invalidField is not null)
            return Results.BadRequest(new { error = "billing-request-invalid", field = invalidField, retryable = false });
        var customer = resolveCustomerReference(context);
        if (customer is null) return Results.Unauthorized();
        if (!IsRequired(customer, 255))
            return Results.Json(new { error = "billing-customer-unavailable", retryable = false }, statusCode: 503);
        try { return await operation(customer); }
        catch (BullgateBillingException exception)
        {
            if (exception.RetryAfter is { } retryAfter)
                context.Response.Headers.RetryAfter = Math.Max(0, Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            // Integration failure must not log the app user out or turn an invalid upstream 200 into success.
            var status = exception.StatusCode is 400 or 404 or 408 or 409 or 422 or 429 or 503
                ? exception.StatusCode.Value : StatusCodes.Status503ServiceUnavailable;
            return Results.Json(new
            {
                error = exception.Error,
                field = exception.Field is "provider" or "productId" or "proof" or "grantId" or "offset" or "limit" ? exception.Field : null,
                exception.Retryable, exception.ResolutionRequired, exception.DeliveryConfirmed,
            }, statusCode: status);
        }
    }

    internal static bool IsRequired(string? value, int maxLength) => value is { Length: > 0 }
        && value.Length <= maxLength && !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    internal static string Policy(BullgateOneTimePurchasePolicy policy) => policy switch
    {
        BullgateOneTimePurchasePolicy.Repeatable => "repeatable",
        BullgateOneTimePurchasePolicy.OncePerCustomer => "once-per-customer",
        _ => throw new BullgateBillingException("billing-response-invalid", true),
    };
}
