namespace Bullgate.Billing.AspNetCore;

/// <summary>Identifies one offer grant for a trusted customer.</summary>
public sealed record BullgateOfferGrantRequest(string CustomerReference, Guid GrantId);
/// <summary>Requests a page of currently available offer grants for a trusted customer.</summary>
public sealed record BullgateOfferGrantListRequest(string CustomerReference, string? Provider = null, int Offset = 0, int Limit = 50);
/// <summary>Requests store-specific preparation for an existing offer grant.</summary>
public sealed record BullgateOfferPreparationRequest(string CustomerReference, Guid GrantId, string Provider);
/// <summary>Defines how a one-time product is represented by its native store.</summary>
public enum BullgateOneTimeStoreProductType
{
    /// <summary>The native store permits repeated consumption.</summary>
    Consumable,
    /// <summary>The native store represents a durable non-consumable purchase.</summary>
    NonConsumable,
}
/// <summary>Contains customer-visible facts for one available offer grant.</summary>
public sealed record BullgateAvailableOfferGrant(Guid GrantId, string OfferKey, string Name, string Category,
    string Provider, string ProductId, string StoreOfferId, string? PurchaseOptionId, string? RedemptionUrl,
    IReadOnlyList<string> Placements, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
/// <summary>Contains one page of customer-visible offer grants.</summary>
public sealed record BullgateAvailableOfferGrants(IReadOnlyList<BullgateAvailableOfferGrant> Items, int? NextOffset);

/// <summary>Server-selected offer checkout. No payment, reservation or redemption is implied.</summary>
public sealed record BullgateOneTimeOfferPreparation(Guid GrantId, string Provider, string ProductId,
    BullgateOneTimeStoreProductType StoreProductType, string StoreOfferId, string? PurchaseOptionId,
    BullgateOneTimePurchasePolicy PurchasePolicy, Guid? StoreAccountToken, DateTimeOffset ExpiresAt, string? RedemptionUrl = null);

public partial interface IBullgateBillingClient
{
    /// <summary>Lists available grants; the customer reference must come from trusted server context.</summary>
    Task<BullgateAvailableOfferGrants> ListAvailableOneTimeOffersAsync(BullgateOfferGrantListRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads one available grant without marking it opened.</summary>
    Task<BullgateAvailableOfferGrant> GetAvailableOneTimeOfferAsync(BullgateOfferGrantRequest request, CancellationToken cancellationToken = default);
    /// <summary>Marks one available grant opened and returns its current public state.</summary>
    Task<BullgateAvailableOfferGrant> OpenOneTimeOfferAsync(BullgateOfferGrantRequest request, CancellationToken cancellationToken = default);
    /// <summary>Prepares exact native-store checkout terms selected by the server.</summary>
    Task<BullgateOneTimeOfferPreparation> PrepareOneTimeOfferAsync(BullgateOfferPreparationRequest request, CancellationToken cancellationToken = default);
}
