using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using static Bullgate.Billing.AspNetCore.BullgateBillingBff;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Maps customer-only one-time offer BFF routes.</summary>
public static class BullgateOneTimeOfferEndpoints
{
    /// <summary>Customer-only offer operations. The host owns cookie/CSRF protection and derives customer from the authenticated session.</summary>
    public static RouteGroupBuilder MapBullgateOneTimeOffers(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveCustomerReference)
    {
        ArgumentNullException.ThrowIfNull(resolveCustomerReference);
        var group = endpoints.MapGroup("/bullgate/billing/v1/offers/one-time/grants")
            .RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(4096));
        group.MapPost("/available", (ListRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference, InvalidList(request), async customer => Results.Ok(
                await client.ListAvailableOneTimeOffersAsync(new(customer, request!.Provider, request.Offset, request.Limit), context.RequestAborted))));
        group.MapPost("/get", (GrantRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference, InvalidGrant(request?.GrantId), async customer => Results.Ok(
                await client.GetAvailableOneTimeOfferAsync(new(customer, request!.GrantId), context.RequestAborted))));
        group.MapPost("/open", (GrantRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference, InvalidGrant(request?.GrantId), async customer => Results.Ok(
                await client.OpenOneTimeOfferAsync(new(customer, request!.GrantId), context.RequestAborted))));
        group.MapPost("/prepare", (PrepareRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference, InvalidGrant(request?.GrantId)
                ?? (request!.Provider is "google-play" or "app-store" ? null : "provider"), async customer =>
            {
                var result = await client.PrepareOneTimeOfferAsync(new(customer, request!.GrantId, request.Provider!), context.RequestAborted);
                return Results.Ok(new
                {
                    result.GrantId, result.Provider, result.ProductId,
                    storeProductType = result.StoreProductType switch
                    {
                        BullgateOneTimeStoreProductType.Consumable => "consumable",
                        BullgateOneTimeStoreProductType.NonConsumable => "non-consumable",
                        _ => throw new BullgateBillingException("billing-response-invalid", true),
                    },
                    result.StoreOfferId, result.PurchaseOptionId,
                    purchasePolicy = Policy(result.PurchasePolicy),
                    storeAccountToken = result.StoreAccountToken?.ToString("D"), result.ExpiresAt, result.RedemptionUrl,
                });
            }));
        // No issue/cancel/inspect route: a customer session must not expose operator permissions or audit data.
        return group;
    }

    private static string? InvalidGrant(Guid? grantId) => grantId is null || grantId == Guid.Empty ? "grantId" : null;
    private static string? InvalidList(ListRequest? request) => request is null ? "body"
        : request.Provider is not (null or "google-play" or "app-store") ? "provider"
        : request.Offset < 0 || request.Offset > int.MaxValue - 101 ? "offset"
        : request.Limit is < 1 or > 100 ? "limit" : null;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ListRequest(string? Provider = null, int Offset = 0, int Limit = 50);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record GrantRequest(Guid GrantId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record PrepareRequest(Guid GrantId, string? Provider);
}
