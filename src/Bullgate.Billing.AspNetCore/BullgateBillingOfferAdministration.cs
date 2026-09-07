using System.Text.Json.Serialization;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Requests a bounded administrative page for a trusted customer.</summary>
public sealed record BullgateOfferManagementListRequest(string CustomerReference, int Offset = 0, int Limit = 50);
/// <summary>Defines an idempotent operator request to issue an offer grant.</summary>
public sealed record BullgateIssueOfferRequest(string CustomerReference, string OfferKey, string IdempotencyKey,
    string ActorReference, string? Reason, DateTimeOffset ExpiresAt, IReadOnlyList<string> Placements);
/// <summary>Identifies the offer grant created or found for an idempotent issue request.</summary>
public sealed record BullgateIssuedOffer(Guid GrantId, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    [property: JsonRequired] bool AlreadyExists);
/// <summary>Defines an operator request to cancel an offer grant.</summary>
public sealed record BullgateCancelOfferRequest(string CustomerReference, Guid GrantId, string ActorReference, string? Reason = null);
/// <summary>Contains the current terminal or non-terminal state of an offer grant.</summary>
public sealed record BullgateOfferStatus(Guid GrantId, string Status);
/// <summary>Contains one server-owned offer definition available for issuance.</summary>
public sealed record BullgateOfferCatalogItem(string OfferKey, string Name, string Category, string Provider,
    string ProductId, string StoreOfferId, string? PurchaseOptionId, string? RedemptionUrl,
    string ProductKey, string EntitlementKey, decimal EntitlementQuantity, [property: JsonRequired] bool IsActive);
/// <summary>Contains one page of offer definitions that may be issued.</summary>
public sealed record BullgateOfferCatalog(IReadOnlyList<BullgateOfferCatalogItem> Items, [property: JsonRequired] int? NextOffset);
/// <summary>Contains administrative state and dates for one offer grant.</summary>
public sealed record BullgateAdministrativeOfferGrant(Guid GrantId, BullgateOfferCatalogItem Offer, string Status,
    IReadOnlyList<string> Placements, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? OpenedAt, DateTimeOffset? CanceledAt, DateTimeOffset? RedeemedAt);
/// <summary>Contains a counted administrative page of offer grants.</summary>
public sealed record BullgateAdministrativeOfferGrants(IReadOnlyList<BullgateAdministrativeOfferGrant> Items,
    [property: JsonRequired] int Total, [property: JsonRequired] int Offset, [property: JsonRequired] int Limit);
/// <summary>Records one immutable administrative action performed on a grant.</summary>
public sealed record BullgateOfferAuditEntry(string Action, string IntegrationClientKey, string ActorReference,
    string? Reason, DateTimeOffset OccurredAt);
/// <summary>Links a redeemed grant to the purchase evidence recorded by Billing.</summary>
public sealed record BullgateOfferRedemptionEvidence(Guid PurchaseId, DateTimeOffset PurchasedAt, DateTimeOffset RecordedAt);
/// <summary>Contains the full administrative audit and optional redemption evidence for a grant.</summary>
public sealed record BullgateOfferGrantHistory(Guid GrantId, string CustomerReference, string OfferKey, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? OpenedAt, DateTimeOffset? CanceledAt,
    IReadOnlyList<BullgateOfferAuditEntry> Audit, BullgateOfferRedemptionEvidence? Redemption);

public partial interface IBullgateBillingClient
{
    /// <summary>Lists offer definitions that an authorized operator may issue to a customer.</summary>
    Task<BullgateOfferCatalog> ListSendableOneTimeOffersAsync(BullgateOfferManagementListRequest request, CancellationToken cancellationToken = default);
    /// <summary>Issues an offer grant using a caller-owned idempotency key and trusted operator identity.</summary>
    Task<BullgateIssuedOffer> IssueOneTimeOfferAsync(BullgateIssueOfferRequest request, CancellationToken cancellationToken = default);
    /// <summary>Cancels an offer grant without exposing the operation through the customer BFF.</summary>
    Task<BullgateOfferStatus> CancelOneTimeOfferAsync(BullgateCancelOfferRequest request, CancellationToken cancellationToken = default);
    /// <summary>Lists historical offer grants for administrative use.</summary>
    Task<BullgateAdministrativeOfferGrants> ListOneTimeOfferHistoryAsync(BullgateOfferManagementListRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads the audit and redemption evidence for one offer grant.</summary>
    Task<BullgateOfferGrantHistory> InspectOneTimeOfferAsync(BullgateOfferGrantRequest request, CancellationToken cancellationToken = default);
}

internal sealed partial class BullgateBillingClient
{
    public Task<BullgateOfferCatalog> ListSendableOneTimeOffersAsync(BullgateOfferManagementListRequest request, CancellationToken cancellationToken = default)
    {
        ValidateManagementList(request);
        return PostAsync<BullgateOfferManagementListRequest, BullgateOfferCatalog>(OffersPath + "catalog", request,
            body => body.Items is not null && body.Items.Count <= request.Limit && body.Items.All(IsManagementOffer)
                && body.Items.All(item => item.IsActive) && body.Items.Select(item => item.OfferKey).Distinct().Count() == body.Items.Count
                && (body.NextOffset is null || body.Items.Count == request.Limit && body.NextOffset == request.Offset + request.Limit), cancellationToken);
    }

    public Task<BullgateIssuedOffer> IssueOneTimeOfferAsync(BullgateIssueOfferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferCustomer(request.CustomerReference);
        ValidateOfferCustomer(request.ActorReference);
        ValidateOfferCustomer(request.IdempotencyKey);
        ValidateManagementReason(request.Reason);
        if (!BullgateBillingServiceCollectionExtensions.IsKey(request.OfferKey) || !IsValidPlacements(request.Placements)
            || !UtcDate(request.ExpiresAt)) throw new ArgumentException("Invalid offer invitation.", nameof(request));
        // Do not require future here: retries after expiration must retrieve the same grant.
        return PostAsync<BullgateIssueOfferRequest, BullgateIssuedOffer>(OffersPath + "issue", request,
            body => body.GrantId != Guid.Empty && IsGrantState(body.Status) && ValidGrantDates(body.CreatedAt, body.ExpiresAt)
                && body.ExpiresAt.UtcTicks / 10 == request.ExpiresAt.UtcTicks / 10, cancellationToken);
    }

    public Task<BullgateOfferStatus> CancelOneTimeOfferAsync(BullgateCancelOfferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferGrant(request.CustomerReference, request.GrantId);
        ValidateOfferCustomer(request.ActorReference);
        ValidateManagementReason(request.Reason);
        return PostAsync<BullgateCancelOfferRequest, BullgateOfferStatus>(OffersPath + "cancel", request,
            body => body.GrantId == request.GrantId && body.Status is "canceled" or "expired" or "redeemed", cancellationToken);
    }

    public Task<BullgateAdministrativeOfferGrants> ListOneTimeOfferHistoryAsync(BullgateOfferManagementListRequest request, CancellationToken cancellationToken = default)
    {
        ValidateManagementList(request);
        return PostAsync<BullgateOfferManagementListRequest, BullgateAdministrativeOfferGrants>(OffersPath + "list", request,
            body => body.Items is not null && body.Total >= 0 && body.Offset == request.Offset && body.Limit == request.Limit
                && body.Items.Count == Math.Min(request.Limit, Math.Max(0, body.Total - request.Offset))
                && body.Items.All(item => item is not null && item.GrantId != Guid.Empty && IsManagementOffer(item.Offer)
                    && IsGrantState(item.Status) && IsValidPlacements(item.Placements) && ValidGrantDates(item.CreatedAt, item.ExpiresAt)
                    && OptionalUtcDate(item.OpenedAt) && OptionalUtcDate(item.CanceledAt) && OptionalUtcDate(item.RedeemedAt))
                && body.Items.Select(item => item.GrantId).Distinct().Count() == body.Items.Count, cancellationToken);
    }

    public Task<BullgateOfferGrantHistory> InspectOneTimeOfferAsync(BullgateOfferGrantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferGrant(request.CustomerReference, request.GrantId);
        return PostAsync<BullgateOfferGrantRequest, BullgateOfferGrantHistory>(OffersPath + "inspect", request,
            body => body.GrantId == request.GrantId && body.CustomerReference == request.CustomerReference
                && BullgateBillingServiceCollectionExtensions.IsKey(body.OfferKey) && IsGrantState(body.Status)
                && ValidGrantDates(body.CreatedAt, body.ExpiresAt) && OptionalUtcDate(body.OpenedAt) && OptionalUtcDate(body.CanceledAt)
                && body.Audit is not null && body.Audit.All(entry => entry is not null && entry.Action is "issued" or "opened" or "canceled"
                    && BullgateBillingServiceCollectionExtensions.IsKey(entry.IntegrationClientKey) && OfferText(entry.ActorReference, 255)
                    && (entry.Reason is null || OfferText(entry.Reason, 1000)) && UtcDate(entry.OccurredAt))
                && (body.Redemption is null || body.Redemption.PurchaseId != Guid.Empty && UtcDate(body.Redemption.PurchasedAt)
                    && UtcDate(body.Redemption.RecordedAt) && body.Redemption.RecordedAt >= body.Redemption.PurchasedAt), cancellationToken);
    }

    private static bool IsManagementOffer(BullgateOfferCatalogItem? item) => item is not null
        && BullgateBillingServiceCollectionExtensions.IsKey(item.OfferKey) && OfferText(item.Name, 160)
        && BullgateBillingServiceCollectionExtensions.IsKey(item.Category) && OfferText(item.ProductId, 255)
        && OfferText(item.StoreOfferId, 255) && BullgateBillingServiceCollectionExtensions.IsKey(item.ProductKey)
        && BullgateBillingServiceCollectionExtensions.IsKey(item.EntitlementKey) && item.EntitlementQuantity is > 0 and < 100000000000000000000m
        && (item.Provider == "google-play" && OfferText(item.PurchaseOptionId, 255) && item.RedemptionUrl is null
            || item.Provider == "app-store" && item.PurchaseOptionId is null && IsAppleRedemptionUrl(item.RedemptionUrl));
    private static bool IsGrantState(string? state) => state is "sent" or "opened" or "expired" or "canceled" or "redeemed";
    private static bool UtcDate(DateTimeOffset date) => date > DateTimeOffset.UnixEpoch && date.Offset == TimeSpan.Zero;
    private static bool OptionalUtcDate(DateTimeOffset? date) => date is null || UtcDate(date.Value);
    private static bool ValidGrantDates(DateTimeOffset created, DateTimeOffset expires) => UtcDate(created) && UtcDate(expires) && expires > created;
    private static void ValidateManagementList(BullgateOfferManagementListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferCustomer(request.CustomerReference);
        if (request.Offset < 0 || request.Offset > int.MaxValue - 101 || request.Limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request));
    }
    private static void ValidateManagementReason(string? reason)
    {
        if (reason is not null && !OfferText(reason, 1000)) throw new ArgumentException("Invalid operator note.", nameof(reason));
    }
}
