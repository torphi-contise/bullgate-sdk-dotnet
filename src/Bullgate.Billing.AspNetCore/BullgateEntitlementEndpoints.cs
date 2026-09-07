using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Maps credential-authenticated entitlement delivery endpoints.</summary>
public static class BullgateEntitlementEndpoints
{
    /// <summary>Maps full reversal of a previously identified delivery.</summary>
    public static IEndpointConventionBuilder MapBullgateEntitlementReversal(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/bullgate/billing/v1/entitlements/reverse", ReverseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(8_192))
            .RequireAuthorization(new AuthorizationPolicyBuilder(BullgateDeliveryAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser().Build());

    /// <summary>Maps idempotent benefit delivery into the consumer application.</summary>
    public static IEndpointConventionBuilder MapBullgateEntitlementDelivery(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/bullgate/billing/v1/entitlements/deliver", DeliverAsync)
            .WithMetadata(new RequestSizeLimitAttribute(8_192))
            .RequireAuthorization(new AuthorizationPolicyBuilder(BullgateDeliveryAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser().Build());

    private static async Task<IResult> DeliverAsync(
        DeliveryRequest request, ClaimsPrincipal principal, HttpResponse response,
        IBullgateEntitlementApplication application, CancellationToken cancellationToken) =>
        await ApplyAsync(request, principal, response, application, false, cancellationToken);

    private static async Task<IResult> ReverseAsync(
        ReversalRequest request, ClaimsPrincipal principal, HttpResponse response,
        IBullgateEntitlementApplication application, CancellationToken cancellationToken) =>
        await ApplyAsync(new(request.OriginalDeliveryId, request.CustomerReference, request.EntitlementKey, request.Quantity),
            principal, response, application, true, cancellationToken);

    private static async Task<IResult> ApplyAsync(
        DeliveryRequest request, ClaimsPrincipal principal, HttpResponse response,
        IBullgateEntitlementApplication application, bool reverse, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        if (request.DeliveryId == Guid.Empty || request.CustomerReference is not { Length: > 0 and <= 255 }
            || string.IsNullOrWhiteSpace(request.CustomerReference) || request.CustomerReference != request.CustomerReference.Trim()
            || !BullgateBillingServiceCollectionExtensions.IsKey(request.EntitlementKey)
            || request.Quantity <= 0 || request.Quantity >= 100000000000000000000m
            || decimal.Round(request.Quantity, 8) != request.Quantity)
            return Results.BadRequest(new { error = "entitlement-request-invalid", retryable = false });

        try
        {
            var result = reverse
                ? await application.ReverseAsync(new BullgateEntitlementReversal(
                    request.DeliveryId, principal.FindFirstValue("billing-environment-key")!,
                    request.CustomerReference, request.EntitlementKey!, request.Quantity), cancellationToken)
                : await application.DeliverAsync(new BullgateEntitlementDelivery(
                request.DeliveryId, principal.FindFirstValue("billing-environment-key")!,
                request.CustomerReference, request.EntitlementKey!, request.Quantity), cancellationToken);
            if (!Enum.IsDefined(result)) throw new InvalidOperationException();
            if (reverse) return Results.Ok(new
            {
                originalDeliveryId = request.DeliveryId,
                status = result == BullgateEntitlementResult.Applied ? "applied" : "already-applied",
            });
            return Results.Ok(new
            {
                deliveryId = request.DeliveryId,
                status = result == BullgateEntitlementResult.Applied ? "applied" : "already-applied",
            });
        }
        catch (BullgateEntitlementRejectedException exception)
        {
            return Results.UnprocessableEntity(new
            {
                error = BullgateBillingClient.IsErrorCode(exception.Error) ? exception.Error : "entitlement-rejected",
                retryable = false,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            response.Headers.RetryAfter = "5";
            return Results.Json(new { error = "entitlement-unavailable", retryable = true }, statusCode: 503);
        }
    }

    private sealed record DeliveryRequest(Guid DeliveryId, string? CustomerReference, string? EntitlementKey, decimal Quantity);
    private sealed record ReversalRequest(Guid OriginalDeliveryId, string? CustomerReference, string? EntitlementKey, decimal Quantity);
}
