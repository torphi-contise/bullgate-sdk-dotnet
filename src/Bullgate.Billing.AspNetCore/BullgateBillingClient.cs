using System.Net.Http.Json;
using System.Text.Json;

namespace Bullgate.Billing.AspNetCore;

internal sealed partial class BullgateBillingClient(HttpClient http) : IBullgateBillingClient
{
    private const string PolicyLimitReached = "billing-purchase-policy-limit-reached";

    public async Task<BullgateOneTimePurchasePreparation> PrepareOneTimePurchaseAsync(
        BullgateOneTimePurchasePreparationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProduct(request.CustomerReference, request.Provider, request.ProductId);
        var body = await PostAsync<BullgateOneTimePurchasePreparationRequest, PreparationResponse>(
            "v1/purchases/one-time/prepare", request, IsValidPreparation, cancellationToken);
        return new(body.PurchasePolicy == "repeatable" ? BullgateOneTimePurchasePolicy.Repeatable : BullgateOneTimePurchasePolicy.OncePerCustomer,
            body.Eligible!.Value, body.Reason, body.StoreAccountToken is null ? null : Guid.ParseExact(body.StoreAccountToken, "D"));
    }

    public async Task<BullgateOneTimePurchaseEligibility> CheckOneTimePurchaseEligibilityAsync(
        BullgateOneTimePurchaseEligibilityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProduct(request.CustomerReference, request.Provider, request.ProductId);
        var body = await PostAsync<BullgateOneTimePurchaseEligibilityRequest, EligibilityResponse>(
            "v1/purchases/one-time/eligibility", request, IsValidEligibility, cancellationToken);
        return new BullgateOneTimePurchaseEligibility(
            body.PurchasePolicy == "repeatable" ? BullgateOneTimePurchasePolicy.Repeatable : BullgateOneTimePurchasePolicy.OncePerCustomer,
            body.Eligible!.Value, body.Reason);
    }

    public async Task<BullgatePurchaseCompletion> CompleteOneTimePurchaseAsync(
        BullgateOneTimePurchaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProduct(request.CustomerReference, request.Provider, request.ProductId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Proof);
        var body = await PostAsync<BullgateOneTimePurchaseRequest, CompletionResponse>(
            "v1/purchases/one-time/complete", request,
            static body => body.Status is "delivered" or "already-delivered", cancellationToken);
        return body.Status == "delivered" ? BullgatePurchaseCompletion.Delivered : BullgatePurchaseCompletion.AlreadyDelivered;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request,
        Func<TResponse, bool> isValidResponse, CancellationToken cancellationToken) where TResponse : class
    {
        try
        {
            using var response = await http.PostAsJsonAsync(path, request, cancellationToken);
            var status = (int)response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
            {
                retryAfter = retryDate - DateTimeOffset.UtcNow;
            }
            if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;

            if (status == 200)
            {
                var body = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
                if (body is null || !isValidResponse(body))
                    throw new BullgateBillingException("billing-response-invalid", true, status, retryAfter);
                return body;
            }

            if (status is 401 or 403)
            {
                throw new BullgateBillingException("billing-integration-unauthorized", false, status);
            }
            ErrorResponse? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken); }
            catch (JsonException) { }
            catch (NotSupportedException) { }

            var retryable = status is 408 or 429 || status >= 500
                || status is not (400 or 404 or 409 or 422)
                || !IsErrorCode(error?.Error) || error?.Retryable == true;
            var code = IsErrorCode(error?.Error) ? error!.Error! : "billing-unavailable";
            // Preserve the known paid-conflict requirement even if an older/inconsistent response omits the flag.
            var resolutionRequired = error?.ResolutionRequired == true || code == PolicyLimitReached;
            throw new BullgateBillingException(code, retryable, status, retryAfter,
                error?.Field is { Length: <= 100 } field ? field : null, resolutionRequired,
                path == "v1/purchases/one-time/complete" && status == 503
                    && code == "billing-store-finalization-pending" && error?.DeliveryConfirmed == true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BullgateBillingException("billing-timeout", true);
        }
        catch (HttpRequestException) { throw new BullgateBillingException("billing-unavailable", true); }
        catch (JsonException) { throw new BullgateBillingException("billing-response-invalid", true); }
        catch (NotSupportedException) { throw new BullgateBillingException("billing-response-invalid", true); }
    }

    internal static bool IsErrorCode(string? value) => value is { Length: > 0 and <= 100 }
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    private static bool IsValidEligibility(EligibilityResponse body) =>
        body.PurchasePolicy is "repeatable" or "once-per-customer"
        && body.Eligible is not null
        && (body.Eligible.Value ? body.Reason is null : IsErrorCode(body.Reason));

    private static bool IsValidPreparation(PreparationResponse body) =>
        IsValidEligibility(new(body.PurchasePolicy, body.Eligible, body.Reason))
        && (body.Eligible == true
            ? body.StoreAccountToken is { Length: 36 } && Guid.TryParseExact(body.StoreAccountToken, "D", out var token) && token != Guid.Empty
            : body.PurchasePolicy == "once-per-customer" && body.Reason == PolicyLimitReached && body.StoreAccountToken is null);

    private static void ValidateProduct(string customerReference, string provider, string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
    }

    private sealed record CompletionResponse(string? Status);
    private sealed record EligibilityResponse(string? PurchasePolicy, bool? Eligible, string? Reason);
    private sealed record PreparationResponse(string? PurchasePolicy, bool? Eligible, string? Reason, string? StoreAccountToken);
    private sealed record ErrorResponse(string? Error, string? Field, bool? Retryable, bool? ResolutionRequired, bool? DeliveryConfirmed);
}
