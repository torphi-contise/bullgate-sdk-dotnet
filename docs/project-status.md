# Project status

Last reviewed against the source baseline dated 2026-09-07.

## Implemented

### Bullgate.Access.AspNetCore

- Typed server-to-server client for registration, login, social authentication,
  session introspection and revocation, recovery, account maintenance,
  application-client configuration, and AccessFlow operations.
- Host-only session and flow cookies.
- ASP.NET Core authentication backed by Access session introspection.
- Authenticated BFF routes under `/bullgate/access/v1`.
- Typed consumer hooks for local principal resolution and product-profile
  provisioning.
- Failure middleware that maps Access unavailability to an HTTP 503 response
  without deleting a potentially valid session cookie.

### Bullgate.Billing.AspNetCore

- Typed server-to-server client for one-time purchase preparation, eligibility,
  completion, catalog reads, purchase history, and customer and administrative
  offer operations.
- Authenticated consumer BFF routes for purchases, catalog reads, and offers.
- Credential-authenticated entitlement delivery and full-reversal endpoints.
- Explicit retry, resolution-required, and delivery-confirmed failure signals.
- Consumer-owned atomic delivery and reversal contract.

## Validation

The repository contains automated tests for both packages. Local build, test,
pack, and package-content verification are required before publication.

Existing laboratory integration demonstrates the code path in a controlled
consumer application. It is not evidence of a public production deployment or
real-store validation.

## Not published

- No versioned package has been published to a public package registry.
- No stable release or compatibility commitment has been announced.
- No SDK runtime is deployed independently; the packages are compiled into
  consumer backends.
- No hosted Bullgate environment is implied by this repository.

## Current maturity

The package version is `0.1.0`. Public APIs may still change before a stable
release. Implemented code may be documented and published while related
products continue to evolve; incompleteness elsewhere is not a publication
blocker and must simply be stated accurately.
