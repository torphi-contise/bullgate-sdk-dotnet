using System.Text.Json.Serialization;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Requests a bounded administrative purchase-history page for a trusted customer.</summary>
public sealed record BullgatePurchaseHistoryRequest(string CustomerReference, int Offset = 0, int Limit = 50);
/// <summary>Read-only facts from Billing. Reversal dates never imply a new grant or permission to finalize at the store.</summary>
public sealed record BullgatePurchaseHistoryItem(Guid PurchaseId, Guid DeliveryId, string Provider,
    string ProductId, string ProductKey, string ProductName, string? ProviderEnvironment,
    int PurchaseQuantity, string EntitlementKey, decimal EntitlementQuantity, string DeliveryState,
    DateTimeOffset PurchasedAt, [property: JsonRequired] decimal? PaidAmount, [property: JsonRequired] string? PaidCurrency,
    [property: JsonRequired] DateTimeOffset? DeliveredAt, [property: JsonRequired] DateTimeOffset? ReversalRequestedAt,
    [property: JsonRequired] DateTimeOffset? ReversedAt);
/// <summary>Contains one page of read-only purchase and delivery facts.</summary>
public sealed record BullgatePurchaseHistoryPage(string CustomerReference,
    IReadOnlyList<BullgatePurchaseHistoryItem> Items, [property: JsonRequired] int? NextOffset);

public partial interface IBullgateBillingClient
{
    /// <summary>Administrative read, requiring billing:purchases:read. Authorize the customer on the consumer server.</summary>
    Task<BullgatePurchaseHistoryPage> ListOneTimePurchaseHistoryAsync(BullgatePurchaseHistoryRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed partial class BullgateBillingClient
{
    public Task<BullgatePurchaseHistoryPage> ListOneTimePurchaseHistoryAsync(BullgatePurchaseHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferCustomer(request.CustomerReference);
        if (request.Offset < 0 || request.Offset > int.MaxValue - 101 || request.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request));
        return PostAsync<BullgatePurchaseHistoryRequest, BullgatePurchaseHistoryPage>("v1/purchases/one-time/history", request,
            body => body.CustomerReference == request.CustomerReference && body.Items is not null
                && body.Items.Count <= request.Limit && body.Items.All(ValidPurchaseHistoryItem)
                && body.Items.Select(item => item.PurchaseId).Distinct().Count() == body.Items.Count
                && (body.NextOffset is null || body.Items.Count == request.Limit && body.NextOffset == request.Offset + request.Limit),
            cancellationToken);
    }

    private static bool ValidPurchaseHistoryItem(BullgatePurchaseHistoryItem? item) => item is not null
        && item.PurchaseId != Guid.Empty && item.DeliveryId != Guid.Empty
        && BullgateBillingServiceCollectionExtensions.IsKey(item.Provider) && OfferText(item.ProductId, 255)
        && BullgateBillingServiceCollectionExtensions.IsKey(item.ProductKey) && OfferText(item.ProductName, 160)
        && (item.ProviderEnvironment is null || OfferText(item.ProviderEnvironment, 40))
        && item.PurchaseQuantity > 0 && BullgateBillingServiceCollectionExtensions.IsKey(item.EntitlementKey)
        && item.EntitlementQuantity is > 0 and < 100000000000000000000m
        && item.DeliveryState is "pending" or "delivered" or "policy-blocked"
        && UtcDate(item.PurchasedAt) && OptionalUtcDate(item.DeliveredAt)
        && (item.DeliveryState == "delivered") == (item.DeliveredAt is not null)
        && item.PaidAmount.HasValue == (item.PaidCurrency is not null)
        && (item.PaidAmount is null || item.PaidAmount >= 0 && OfferText(item.PaidCurrency, 10))
        && OptionalUtcDate(item.ReversalRequestedAt) && OptionalUtcDate(item.ReversedAt)
        && (item.ReversedAt is null || item.ReversalRequestedAt is not null && item.ReversedAt >= item.ReversalRequestedAt)
        && (item.DeliveryState != "policy-blocked" || item.ReversalRequestedAt is null);
}
