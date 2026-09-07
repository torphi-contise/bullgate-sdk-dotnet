namespace Bullgate.Billing.AspNetCore;

internal sealed partial class BullgateBillingClient
{
    private const string OffersPath = "v1/offers/one-time/grants/";

    public async Task<BullgateAvailableOfferGrants> ListAvailableOneTimeOffersAsync(BullgateOfferGrantListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferCustomer(request.CustomerReference);
        if (request.Provider is not (null or "google-play" or "app-store")) throw new ArgumentException("Unsupported provider.", nameof(request));
        if (request.Offset < 0 || request.Offset > int.MaxValue - 101 || request.Limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request));
        var body = await PostAsync<BullgateOfferGrantListRequest, OfferListResponse>(OffersPath + "available", request,
            body => body.Items is not null && body.Items.Count <= request.Limit
                && body.Items.All(item => IsValidOffer(item) && (request.Provider is null || item!.Provider == request.Provider))
                && body.Items.Select(item => item!.GrantId).Distinct().Count() == body.Items.Count
                && (body.NextOffset is null || body.Items.Count == request.Limit && body.NextOffset == request.Offset + request.Limit), cancellationToken);
        return new(body.Items!.Select(item => Offer(item!)).ToArray(), body.NextOffset);
    }

    public Task<BullgateAvailableOfferGrant> GetAvailableOneTimeOfferAsync(BullgateOfferGrantRequest request, CancellationToken cancellationToken = default) =>
        GetOfferAsync("get", request, cancellationToken);

    public Task<BullgateAvailableOfferGrant> OpenOneTimeOfferAsync(BullgateOfferGrantRequest request, CancellationToken cancellationToken = default) =>
        GetOfferAsync("open", request, cancellationToken);

    private async Task<BullgateAvailableOfferGrant> GetOfferAsync(string operation, BullgateOfferGrantRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferGrant(request.CustomerReference, request.GrantId);
        var body = await PostAsync<BullgateOfferGrantRequest, OfferResponse>(OffersPath + operation, request,
            body => IsValidOffer(body) && body.GrantId == request.GrantId && (operation != "open" || body.Status == "opened"), ct);
        return Offer(body);
    }

    public async Task<BullgateOneTimeOfferPreparation> PrepareOneTimeOfferAsync(BullgateOfferPreparationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOfferGrant(request.CustomerReference, request.GrantId);
        if (request.Provider is not ("google-play" or "app-store")) throw new ArgumentException("Unsupported provider.", nameof(request));
        var body = await PostAsync<BullgateOfferPreparationRequest, OfferPreparationResponse>(OffersPath + "prepare", request,
            body => body.GrantId == request.GrantId && body.Provider == request.Provider
                && OfferText(body.ProductId, 255) && OfferText(body.StoreOfferId, 255)
                && body.StoreProductType is "consumable" or "non-consumable"
                && body.PurchasePolicy is "repeatable" or "once-per-customer"
                && body.ExpiresAt.Offset == TimeSpan.Zero && body.ExpiresAt > DateTimeOffset.UtcNow
                && (body.Provider == "google-play" && OfferText(body.PurchaseOptionId, 255)
                    && body.StoreAccountToken is not null && body.StoreAccountToken != Guid.Empty && body.RedemptionUrl is null
                    || body.Provider == "app-store" && body.PurchaseOptionId is null && body.StoreAccountToken is null
                    && IsAppleRedemptionUrl(body.RedemptionUrl)),
            cancellationToken);
        return new(body.GrantId, body.Provider!, body.ProductId!,
            body.StoreProductType == "consumable" ? BullgateOneTimeStoreProductType.Consumable : BullgateOneTimeStoreProductType.NonConsumable,
            body.StoreOfferId!, body.PurchaseOptionId,
            body.PurchasePolicy == "repeatable" ? BullgateOneTimePurchasePolicy.Repeatable : BullgateOneTimePurchasePolicy.OncePerCustomer,
            body.StoreAccountToken, body.ExpiresAt, body.RedemptionUrl);
    }

    private static bool IsValidOffer(OfferResponse? body) => body is not null && body.GrantId != Guid.Empty
        && BullgateBillingServiceCollectionExtensions.IsKey(body.OfferKey) && OfferText(body.Name, 160)
        && BullgateBillingServiceCollectionExtensions.IsKey(body.Category) && OfferText(body.ProductId, 255) && OfferText(body.StoreOfferId, 255)
        && IsValidPlacements(body.Placements) && body.Status is "sent" or "opened"
        && body.CreatedAt > DateTimeOffset.UnixEpoch && body.CreatedAt.Offset == TimeSpan.Zero
        && body.ExpiresAt > body.CreatedAt && body.ExpiresAt.Offset == TimeSpan.Zero
        && (body.Provider == "google-play" && OfferText(body.PurchaseOptionId, 255) && body.RedemptionUrl is null
            || body.Provider == "app-store" && body.PurchaseOptionId is null && IsAppleRedemptionUrl(body.RedemptionUrl));

    private static bool IsAppleRedemptionUrl(string? value) => OfferText(value, 2048)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "apps.apple.com" && uri.AbsolutePath == "/redeem" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static BullgateAvailableOfferGrant Offer(OfferResponse body) => new(body.GrantId, body.OfferKey!, body.Name!, body.Category!,
        body.Provider!, body.ProductId!, body.StoreOfferId!, body.PurchaseOptionId, body.RedemptionUrl,
        body.Placements!.Select(value => value!).ToArray(), body.Status!, body.CreatedAt, body.ExpiresAt);

    private static bool IsValidPlacements(IReadOnlyList<string?>? placements) => placements is { Count: <= 16 }
        && placements.All(BullgateBillingServiceCollectionExtensions.IsKey)
        && placements.Distinct(StringComparer.Ordinal).Count() == placements.Count;

    private static bool OfferText(string? value, int max) => value is { Length: > 0 } && value.Length <= max
        && !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);
    private static void ValidateOfferCustomer(string customer)
    {
        if (!OfferText(customer, 255)) throw new ArgumentException("Invalid customer reference.", nameof(customer));
    }
    private static void ValidateOfferGrant(string customer, Guid grantId)
    {
        ValidateOfferCustomer(customer);
        if (grantId == Guid.Empty) throw new ArgumentException("Invalid grant id.", nameof(grantId));
    }

    private sealed record OfferListResponse(IReadOnlyList<OfferResponse?>? Items, int? NextOffset);
    private sealed record OfferResponse(Guid GrantId, string? OfferKey, string? Name, string? Category,
        string? Provider, string? ProductId, string? StoreOfferId, string? PurchaseOptionId, string? RedemptionUrl,
        IReadOnlyList<string?>? Placements, string? Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
    private sealed record OfferPreparationResponse(Guid GrantId, string? Provider, string? ProductId, string? StoreProductType,
        string? StoreOfferId, string? PurchaseOptionId, string? PurchasePolicy, Guid? StoreAccountToken, DateTimeOffset ExpiresAt,
        string? RedemptionUrl);
}
