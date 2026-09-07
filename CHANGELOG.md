# Changelog

This changelog records user-visible changes to the packages in this repository.
The two packages are versioned independently because neither package depends on
the other.

The format is based on Keep a Changelog and versions follow Semantic
Versioning. A version marked **Unreleased** has not been published to a package
registry, even when the same version already appears in a project file.

## Bullgate.Access.AspNetCore

### 0.1.0 - Unreleased

#### Added

- Typed server-to-server client for email/password registration and login,
  Google and Apple authentication, session introspection and revocation,
  password recovery, identity contact reads, account maintenance, and account
  deletion.
- AccessFlow protocol version 1 client with idempotent request identifiers,
  optimistic revision checks, temporary bearer capabilities, and terminal
  session handling.
- Public application-client configuration lookup by stable client key.
- ASP.NET Core authentication handler that converts an online-introspected
  product session into a consumer-owned local principal.
- Separate host-only, `HttpOnly` cookies for sessions and AccessFlow
  capabilities.
- Consumer extension points for local principal resolution and idempotent
  product-profile provisioning.
- Authenticated consumer BFF routes for registration, login, social
  authentication, session lookup, logout, recovery, account maintenance,
  application-client configuration, and AccessFlow operations.
- Failure middleware that returns a no-store HTTP 503 response while preserving
  a session cookie when Access is unavailable.
- Package-specific README and generated XML documentation for public APIs.
- English integration, HTTP, security, architecture, failure, and project
  status documentation.

#### Security

- Session tokens and flow capabilities remain outside JSON responses.
- Newly issued sessions are revoked when the consumer cannot establish the
  required product profile.
- Unknown or malformed upstream responses are treated as availability failures
  instead of trusted business rejections.
- The consumer application retains responsibility for HTTPS, CSRF protection,
  CORS, local authorization, product persistence, and cross-database deletion
  policy.

No public package, Git tag, or GitHub release exists for this version yet.

## Bullgate.Billing.AspNetCore

### 0.1.0 - Unreleased

Billing remains in active development. Its initial release contents and release
notes are not frozen and will be curated during the dedicated Billing
documentation and release-preparation work.

No public package, Git tag, or GitHub release exists for this version yet.

## Changelog rules

- Add entries under the affected package, not under a repository-wide version.
- Record behavior visible to an integrator: public API, HTTP contract, security
  boundary, package metadata, compatibility, or operational requirement.
- Do not record formatting, refactoring, or test-only changes unless they alter
  the public contract or release confidence.
- Use `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, and
  `Security` categories as applicable.
- Move entries from an unreleased version to a dated version only when that
  exact package artifact is published.
- Never rewrite the contents of an already published version. Correct an error
  in a new patch release.
