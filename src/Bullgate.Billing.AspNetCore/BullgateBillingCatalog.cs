namespace Bullgate.Billing.AspNetCore;

/// <summary>Requests the one-time commercial catalog for a supported store provider.</summary>
public sealed record BullgateOneTimeCatalogRequest(string Provider);

/// <summary>Commercial definition from Billing. Inactive known SKUs remain present for paid-purchase recovery.</summary>
public sealed record BullgateOneTimeCatalogProduct(string ProductKey, string Name, string Provider,
    string ProductId, BullgateOneTimeStoreProductType StoreProductType,
    BullgateOneTimePurchasePolicy PurchasePolicy, string EntitlementKey, decimal EntitlementQuantity, bool IsActive);

/// <summary>Contains Billing-owned one-time product definitions, including inactive recovery entries.</summary>
public sealed record BullgateOneTimeCatalog(IReadOnlyList<BullgateOneTimeCatalogProduct> Items);

public partial interface IBullgateBillingClient
{
    /// <summary>Lists the authoritative commercial catalog for a store provider.</summary>
    Task<BullgateOneTimeCatalog> ListOneTimeProductsAsync(BullgateOneTimeCatalogRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed partial class BullgateBillingClient
{
    public async Task<BullgateOneTimeCatalog> ListOneTimeProductsAsync(BullgateOneTimeCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Provider is not ("app-store" or "google-play"))
            throw new ArgumentException("Unsupported provider.", nameof(request));
        var body = await PostAsync<BullgateOneTimeCatalogRequest, CatalogResponse>("v1/catalog/one-time", request,
            result => result.Items is not null
                && result.Items.All(item => IsCatalogProduct(item, request.Provider))
                && result.Items.Select(item => item!.ProductId).Distinct(StringComparer.Ordinal).Count() == result.Items.Count,
            cancellationToken);
        return new(body.Items!.Select(item => new BullgateOneTimeCatalogProduct(item!.ProductKey!, item.Name!,
            item.Provider!, item.ProductId!,
            item.StoreProductType == "consumable" ? BullgateOneTimeStoreProductType.Consumable : BullgateOneTimeStoreProductType.NonConsumable,
            item.PurchasePolicy == "repeatable" ? BullgateOneTimePurchasePolicy.Repeatable : BullgateOneTimePurchasePolicy.OncePerCustomer,
            item.EntitlementKey!, item.EntitlementQuantity!.Value, item.IsActive!.Value)).ToArray());
    }

    private static bool IsCatalogProduct(CatalogProductResponse? item, string provider) => item is not null
        && BullgateBillingServiceCollectionExtensions.IsKey(item.ProductKey) && OfferText(item.Name, 160)
        && item.Provider == provider && OfferText(item.ProductId, 255)
        && item.StoreProductType is "consumable" or "non-consumable"
        && item.PurchasePolicy is "repeatable" or "once-per-customer"
        && BullgateBillingServiceCollectionExtensions.IsKey(item.EntitlementKey)
        && item.EntitlementQuantity is > 0 and < 100000000000000000000m && item.IsActive is not null;

    private sealed record CatalogResponse(IReadOnlyList<CatalogProductResponse?>? Items);
    private sealed record CatalogProductResponse(string? ProductKey, string? Name, string? Provider, string? ProductId,
        string? StoreProductType, string? PurchasePolicy, string? EntitlementKey, decimal? EntitlementQuantity, bool? IsActive);
}
