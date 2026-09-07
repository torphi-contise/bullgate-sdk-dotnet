namespace Bullgate.Billing.AspNetCore;

/// <summary>Provides trusted customer data and store proof for one-time purchase completion.</summary>
public sealed record BullgateOneTimePurchaseRequest(
    string CustomerReference, string Provider, string ProductId, string Proof);

/// <summary>Describes whether a benefit was delivered by this call or an earlier idempotent call.</summary>
public enum BullgatePurchaseCompletion
{
    /// <summary>The benefit was delivered by the completed operation.</summary>
    Delivered,
    /// <summary>The same purchase had already been delivered.</summary>
    AlreadyDelivered,
}

/// <summary>Identifies a customer and store product for a read-only eligibility check.</summary>
public sealed record BullgateOneTimePurchaseEligibilityRequest(
    string CustomerReference, string Provider, string ProductId);

/// <summary>Defines the server-owned repurchase policy for a one-time product.</summary>
public enum BullgateOneTimePurchasePolicy
{
    /// <summary>The customer may buy the product repeatedly.</summary>
    Repeatable,
    /// <summary>The customer may receive the product only once.</summary>
    OncePerCustomer,
}

/// <summary>A read-only observation, not a payment authorization or reservation.</summary>
public sealed record BullgateOneTimePurchaseEligibility(
    BullgateOneTimePurchasePolicy PurchasePolicy, bool Eligible, string? Reason);

public sealed record BullgateOneTimePurchasePreparationRequest(
    string CustomerReference, string Provider, string ProductId);

/// <summary>Not a reservation. Pass StoreAccountToken to the native store only when Eligible is true.</summary>
public sealed record BullgateOneTimePurchasePreparation(
    BullgateOneTimePurchasePolicy PurchasePolicy, bool Eligible, string? Reason, Guid? StoreAccountToken);

/// <summary>Provides typed server-to-server operations for Bullgate Billing.</summary>
public partial interface IBullgateBillingClient
{
    /// <summary>
    /// Prepare a new purchase: eligibility plus a stable pseudonymous identity for the native store.
    /// CustomerReference must come from the authenticated server session, not arbitrary app input.
    /// Does not charge, reserve a unit or grant a benefit; completion still rechecks the policy.
    /// </summary>
    Task<BullgateOneTimePurchasePreparation> PrepareOneTimePurchaseAsync(
        BullgateOneTimePurchasePreparationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check before initiating a new payment. A negative result must not prevent recovery of an existing transaction.
    /// Eligibility can change concurrently; completion still enforces the authoritative policy.
    /// </summary>
    Task<BullgateOneTimePurchaseEligibility> CheckOneTimePurchaseEligibilityAsync(
        BullgateOneTimePurchaseEligibilityRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a store proof and completes idempotent benefit delivery. Recovery
    /// calls submit the existing proof directly without a new eligibility gate.
    /// </summary>
    Task<BullgatePurchaseCompletion> CompleteOneTimePurchaseAsync(
        BullgateOneTimePurchaseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Represents a structured Billing rejection or availability failure.</summary>
public sealed class BullgateBillingException(
    string error, bool retryable, int? statusCode = null, TimeSpan? retryAfter = null,
    string? field = null, bool resolutionRequired = false, bool deliveryConfirmed = false) : Exception("The Bullgate Billing request could not be completed.")
{
    /// <summary>Gets the stable machine-readable Billing error code.</summary>
    public string Error { get; } = error;
    /// <summary>Gets whether repeating the same operation may succeed.</summary>
    public bool Retryable { get; } = retryable;
    /// <summary>Gets the upstream HTTP status when available.</summary>
    public int? StatusCode { get; } = statusCode;
    /// <summary>Gets the requested minimum retry delay when available.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
    /// <summary>Gets the rejected public field name when available.</summary>
    public string? Field { get; } = field;
    /// <summary>
    /// The transaction needs resolution beyond retrying this request; it is not a rejected payment or a refund.
    /// False means no such requirement was reported, not permission to discard or finalize a purchase.
    /// Retryable alone never confirms delivery or authorizes store finalization.
    /// </summary>
    public bool ResolutionRequired { get; } = resolutionRequired;
    /// <summary>Benefit delivery was confirmed, but the same completion request must be retried to finish at the store.</summary>
    public bool DeliveryConfirmed { get; } = deliveryConfirmed;
}

/// <summary>Describes an idempotent benefit grant requested by Billing.</summary>
public sealed record BullgateEntitlementDelivery(
    Guid DeliveryId, string EnvironmentKey, string CustomerReference,
    string EntitlementKey, decimal Quantity);

/// <summary>Full reversal of the original grant, not a negative new delivery or a partial refund.</summary>
public sealed record BullgateEntitlementReversal(
    Guid DeliveryId, string EnvironmentKey, string CustomerReference,
    string EntitlementKey, decimal Quantity);

/// <summary>Describes whether an entitlement mutation was newly or previously applied.</summary>
public enum BullgateEntitlementResult
{
    /// <summary>The consumer committed the mutation and receipt.</summary>
    Applied,
    /// <summary>The same delivery or reversal receipt had already been committed.</summary>
    AlreadyApplied,
}

/// <summary>Applies Billing deliveries and reversals to consumer-owned business persistence.</summary>
public interface IBullgateEntitlementApplication
{
    /// <summary>
    /// Persist the benefit and DeliveryId receipt atomically in the consumer database.
    /// Repeating a delivery must not grant again; different deliveries of the same product can grant again.
    /// Reject a reused DeliveryId with different payload. Keep a replay guard after account deletion.
    /// Return only after commit; the SDK does not provide a database or in-memory deduplication.
    /// </summary>
    Task<BullgateEntitlementResult> DeliverAsync(
        BullgateEntitlementDelivery delivery, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically reverse the original delivery using the consumer's business rule. Deduplicate by
    /// EnvironmentKey + DeliveryId, including a zero adjustment. Quantity is the original grant.
    /// Retain the original replay guard. A reversal may precede confirmation of delivery: serialize
    /// the two operations and prevent a delayed grant after reversal. Return only after commit.
    /// </summary>
    Task<BullgateEntitlementResult> ReverseAsync(
        BullgateEntitlementReversal reversal, CancellationToken cancellationToken);
}

/// <summary>Represents a non-retryable business rejection from the consumer entitlement adapter.</summary>
public sealed class BullgateEntitlementRejectedException(string error)
    : Exception("The consumer rejected the entitlement delivery.")
{
    /// <summary>Gets the consumer-defined machine-readable rejection code.</summary>
    public string Error { get; } = error;
}
