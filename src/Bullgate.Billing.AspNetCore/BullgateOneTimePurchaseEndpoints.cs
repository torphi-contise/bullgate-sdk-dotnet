using static Bullgate.Billing.AspNetCore.BullgateBillingBff;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Maps authenticated one-time purchase and catalog BFF routes.</summary>
public static class BullgateOneTimePurchaseEndpoints
{
    /// <summary>
    /// Maps the authenticated consumer BFF, without depending on Access or a product database.
    /// Resolve the customer from the server session, never from request data; null rejects the session.
    /// The host owns authentication and CSRF protection when using browser cookies.
    /// </summary>
    public static RouteGroupBuilder MapBullgateOneTimePurchases(
        this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveCustomerReference)
    {
        ArgumentNullException.ThrowIfNull(resolveCustomerReference);
        var group = endpoints.MapGroup("/bullgate/billing/v1/purchases/one-time")
            .RequireAuthorization()
            .WithMetadata(new RequestSizeLimitAttribute(131_072));

        // Shopping and recovery share the same server catalog. No second commercial catalog in the host.
        endpoints.MapPost("/bullgate/billing/v1/catalog/one-time",
            (CatalogRequest? request, HttpContext context, IBullgateBillingClient client) =>
                ExecuteAsync(context, resolveCustomerReference,
                    request is null ? "body" : request.Provider is "app-store" or "google-play" ? null : "provider",
                    async _ =>
                    {
                        var catalog = await client.ListOneTimeProductsAsync(new(request!.Provider!), context.RequestAborted);
                        return Results.Ok(new { items = catalog.Items.Select(item => new
                        {
                            item.ProductKey, item.Name, item.Provider, item.ProductId,
                            storeProductType = item.StoreProductType == BullgateOneTimeStoreProductType.Consumable
                                ? "consumable" : "non-consumable",
                            purchasePolicy = Policy(item.PurchasePolicy),
                            item.EntitlementKey, item.EntitlementQuantity, item.IsActive,
                        }) });
                    }))
            .RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(4096));

        group.MapPost("/prepare", (ProductRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference, InvalidProduct(request?.Provider, request?.ProductId), async customer =>
            {
                var result = await client.PrepareOneTimePurchaseAsync(
                    new(customer, request!.Provider!, request.ProductId!), context.RequestAborted);
                return Results.Ok(new
                {
                    purchasePolicy = Policy(result.PurchasePolicy),
                    result.Eligible,
                    result.Reason,
                    storeAccountToken = result.StoreAccountToken?.ToString("D"),
                });
            }));

        group.MapPost("/eligibility", (ProductRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference, InvalidProduct(request?.Provider, request?.ProductId), async customer =>
            {
                var result = await client.CheckOneTimePurchaseEligibilityAsync(
                    new(customer, request!.Provider!, request.ProductId!), context.RequestAborted);
                return Results.Ok(new { purchasePolicy = Policy(result.PurchasePolicy), result.Eligible, result.Reason });
            }));

        group.MapPost("/complete", (CompletionRequest? request, HttpContext context, IBullgateBillingClient client) =>
            ExecuteAsync(context, resolveCustomerReference,
                InvalidProduct(request?.Provider, request?.ProductId) ?? (IsRequired(request?.Proof, 65_536) ? null : "proof"),
                async customer =>
                {
                    // Recovery must not prepare again or recheck eligibility before submitting a paid transaction.
                    var result = await client.CompleteOneTimePurchaseAsync(
                        new(customer, request!.Provider!, request.ProductId!, request.Proof!), context.RequestAborted);
                    return Results.Ok(new
                    {
                        status = result switch
                        {
                            BullgatePurchaseCompletion.Delivered => "delivered",
                            BullgatePurchaseCompletion.AlreadyDelivered => "already-delivered",
                            _ => throw new BullgateBillingException("billing-response-invalid", true),
                        },
                    });
                }));

        return group;
    }

    private static string? InvalidProduct(string? provider, string? productId) =>
        !BullgateBillingServiceCollectionExtensions.IsKey(provider) ? "provider"
        : !IsRequired(productId, 255) ? "productId" : null;

    // Reject injected customer/environment/benefit fields instead of accepting a server-to-server payload from the app.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ProductRequest(string? Provider, string? ProductId);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record CatalogRequest(string? Provider);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record CompletionRequest(string? Provider, string? ProductId, string? Proof);
}
