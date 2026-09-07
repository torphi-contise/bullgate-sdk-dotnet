# Failure model

## Access

`BullgateAccessRejectedException` represents a valid rejection returned by
Access and preserves the HTTP status, error code, and optional field.

`BullgateAccessUnavailableException` represents timeout, transport failure, or
an invalid/unusable upstream response. `UseBullgateAccessFailures` maps it to
HTTP 503 with `access-unavailable` when the response has not started. The
session cookie is preserved.

`BullgateApplicationRejectedException` is available to consumer adapters that
must return a structured product-level rejection during profile provisioning.

## Billing

`BullgateBillingException` exposes:

- `Error`: stable machine-readable code;
- `Retryable`: whether repeating the same operation may succeed;
- `StatusCode`: upstream status when available;
- `RetryAfter`: minimum delay requested by the service;
- `Field`: optional public invalid-field name;
- `ResolutionRequired`: the transaction needs explicit resolution beyond a
  retry;
- `DeliveryConfirmed`: delivery succeeded but provider finalization remains
  pending.

`Retryable` alone never proves delivery and never authorizes store
finalization. Absence of `DeliveryConfirmed` does not prove that delivery
failed. Preserve the same proof and idempotency data while retrying.

The consumer BFF exposes only approved public field names and converts upstream
authentication failures into integration failures. It must not log the end user
out because a server-to-server credential is invalid.

## Cancellation and timeouts

A caller-requested cancellation remains an `OperationCanceledException`.
Billing request timeouts become `billing-timeout`; transport failures become
`billing-unavailable`. Applications should distinguish these outcomes from a
business rejection and avoid starting a second payment blindly.
