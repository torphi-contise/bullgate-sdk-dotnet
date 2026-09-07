# Access troubleshooting

This guide diagnoses the ASP.NET Core adapter exposed by
`Bullgate.Access.AspNetCore`. Start with the observed HTTP response or startup
failure, then verify the consumer boundary before changing Access configuration.

Never paste an integration credential, session token, provider token, recovery
token, OTP, or AccessFlow capability into an issue, log, screenshot, or support
message.

## Application fails during startup

`AddBullgateAccess` validates its configuration when the host starts.

### BaseAddress must be an absolute URI

Expected configuration:

```csharp
options.BaseAddress = new Uri("https://access.example.test");
```

Relative URLs and null values fail validation. Verify the effective
configuration after environment-variable and secret-provider binding. Do not
print the integration credential while inspecting configuration.

### IntegrationCredential must be a bgic_ credential

The configured value must begin with `bgic_`. Store the complete credential in
the consumer server's secret manager. Do not use a delivery credential, a
session token, a mobile configuration value, or the public application-client
key.

A value with the correct prefix can still be revoked, expired, or scoped to the
wrong realm or environment. Prefix validation proves only the local format.

### RequestTimeout must be positive

The timeout must be greater than zero. The default is ten seconds. Increasing
it may hide network or service latency and does not correct an unreachable URL,
invalid credential, or malformed upstream response.

### Cookie names are invalid

`CookieName` and `FlowCookieName` must both be non-empty and must be
different. The defaults are `bullgate.session` and `bullgate.flow`.

Changing a cookie name invalidates the host's ability to read cookies written
under the old name. Plan the session impact before changing production values.

## Required service cannot be resolved

The Access package registers the client and cookie services, but the consumer
must register implementations of:

- `IBullgatePrincipalResolver`;
- `IBullgateAccessApplication<TRegistration,TApplication>`.

The generic types used during registration must match the types passed to
`MapBullgateAccess<TRegistration,TApplication>()`. A mismatch creates a
different service type and dependency injection cannot resolve it.

Register authentication with `AddBullgateSession()` after selecting the
Bullgate authentication scheme or as part of the host's authentication
configuration.

## access-unavailable with HTTP 503

The adapter uses `access-unavailable` when it cannot obtain a trustworthy
Access result. Causes include:

- request timeout;
- connection or DNS failure;
- TLS or proxy failure;
- unreadable response content;
- empty or invalid JSON;
- an incomplete success response;
- an unsupported session purpose or application-client platform;
- an unrecognized upstream status or error body;
- a completed flow that returns an inactive or non-product session.

Check the server-side exception log produced by
`UseBullgateAccessFailures()`, the configured base address, network path, and
Access health. Correlate with Access logs using safe request metadata; do not
log bearer material.

An HTTP 503 does not prove that the user's session is invalid. The middleware
preserves the session cookie intentionally. Do not clear the cookie, create a
second identity, or repeat a non-idempotent consumer mutation merely because
Access was temporarily unavailable.

Place `UseBullgateAccessFailures()` before authentication and endpoint
execution so it can translate `BullgateAccessUnavailableException` before the
HTTP response starts.

## verification-delivery-unavailable with HTTP 503

This is a structured Access rejection, not the generic adapter availability
response. It means Access recognized the verification operation but could not
deliver the verification message.

Return the code to the application client according to the Access contract.
Preserve cooldown and attempt semantics. Do not convert it into success, issue a
local OTP, or treat it as proof that the identity or phone is invalid.

## GET /session returns HTTP 401

Check these conditions in order:

1. the request includes the configured session cookie;
2. the cookie has not expired and its path is `/`;
3. Access introspection returns an active session;
4. the consumer can resolve the product profile;
5. `IBullgatePrincipalResolver` returns a non-empty local subject when a
   product principal is required.

An inactive session causes the adapter to delete the cookie. A null or empty
local principal also deletes it because a Bullgate identity without a trusted
consumer subject must not authenticate the product.

A registration-purpose session may still be visible through `GET /session`
when its consumer profile resolves, but it does not authenticate protected
product routes. This distinction is intentional.

## A protected product route returns HTTP 401

A valid Bullgate identity is not sufficient. Product authentication requires:

- an active online-introspected session;
- `BullgateSessionPurpose.Product`;
- a trusted local result from `IBullgatePrincipalResolver`;
- a non-empty local `Subject`.

Inspect `IBullgateSessionFeature` during controlled debugging to distinguish
an absent session from a registration-purpose session. Never expose that
feature or its data through an unrestricted diagnostic endpoint.

## A protected product route returns HTTP 403

HTTP 403 means authentication succeeded but the host's authorization policy
rejected the principal. Inspect the consumer-owned policy and the claims
returned by `IBullgatePrincipalResolver`.

The SDK adds name identifier, email, Bullgate identity ID, optional session ID,
optional name, and trusted additional claims. Authorization rules remain the
consumer application's responsibility.

## The session cookie is not stored by the browser

The default cookie policy is `Secure=Always`. A browser will not return a
secure cookie over plain HTTP. Use HTTPS for deployed environments.

For explicit local HTTP development only:

```csharp
options.CookieSecurePolicy = CookieSecurePolicy.None;
```

Do not carry that setting into a deployed environment.

Also verify:

- the client sends requests with credentials when crossing origins;
- CORS allows the exact trusted origin and credentials;
- the reverse proxy reports the original HTTPS scheme correctly when using
  `CookieSecurePolicy.SameAsRequest`;
- the host name is stable;
- the cookie has not expired;
- the response actually contains `Set-Cookie`.

The adapter intentionally omits the Domain attribute, making cookies host-only.

## The AccessFlow cookie is not sent

The flow capability cookie is deliberately scoped to
`/bullgate/access/v1`. It will not be sent to unrelated product routes.

Check the configured `FlowCookieName`, HTTPS and SameSite behavior, browser
credential mode, host name, and expiration. Starting or reading a flow refreshes
the cookie from the Access response. Establishing the product session from a
completed flow deletes it.

The `flowId` in the path is not authentication. The matching capability cookie
is still required.

## Registration returns missing-fields for application

`POST /register` requires the consumer-defined `application` payload. Social
authentication also requires it when the Bullgate identity has no existing
consumer product profile.

Send the shape represented by the exact `TRegistration` used in
`MapBullgateAccess<TRegistration,TApplication>()`. Do not put identity-owned
email, phone, verification, or linked-provider state into the product payload.

## Login returns application-not-provisioned

Access authenticated the identity, but
`IBullgateAccessApplication.ResolveAsync` returned null. The adapter revokes
the newly issued session instead of granting access without a product profile.

Check the consumer database, identity-to-profile mapping, realm/environment
selection, and resolver implementation. Do not create a placeholder principal
or copy identity data into a new product profile merely to bypass the error.

If this follows a partially completed registration, verify that
`ProvisionAsync` is idempotent and that the registration retry can resume
safely.

## Account operation returns session-purpose-invalid

Password, email, and provider-link management require a product-purpose
session. Finish the required registration flow rather than overriding the
purpose locally.

The JSON body cannot select another session. These operations read the opaque
token from the host-only session cookie.

## application-client-invalid

Phone recovery and AccessFlow start require a non-empty
`applicationClientKey`. Use the stable public key configured for the client,
such as an approved Android, iOS, or web client key. Do not use an internal
database ID, integration credential, application ID, or package name unless it
is also the configured client key.

If a non-empty key is rejected by Access, verify that it exists in the realm and
environment selected by the integration credential.

## AccessFlow action fails

Check that the request uses:

- the current `flowId`;
- the capability cookie issued for that flow;
- a new idempotent `requestId` for a new intent, or the original ID for an
  exact retry;
- the exact current `expectedRevision`;
- an action ID and type advertised by the current snapshot;
- input matching that action's contract.

A missing action produces `invalid-request` with field `action`. Revision,
action, state, attempt, cooldown, or capability rejections come from Access and
retain their structured status and error. Use the
[Access service error catalog](https://github.com/torphi-contise/bullgate-access/blob/main/docs/http-api.md)
instead of guessing their meaning.

Do not automatically submit an action again with a new request ID after an
ambiguous transport failure. First read the flow using the same capability and
inspect the current revision.

## Flow completes but the product session is not established

The adapter accepts a terminal session only when:

1. the flow reports a product-purpose issued session;
2. online introspection confirms that session is active and product-purpose;
3. the consumer resolves the required product profile.

An invalid or inactive issued session becomes `access-unavailable`. A missing
consumer profile returns `application-not-provisioned` and revokes the issued
session.

Do not copy the issued token from logs or bypass introspection. The token and
capability are intentionally absent from the public JSON response.

## Logout does not clear the cookie during an outage

Logout revokes the Access session before deleting the local cookies. If
revocation fails with an availability error, the middleware returns HTTP 503
and the cookies remain. This preserves the ability to retry the same revocation
rather than presenting local logout as globally complete.

The application may offer a separate local-device cleanup choice, but it must
describe that behavior honestly and must not claim that the Access session was
revoked.

## DELETE /bullgate/access/v1/account returns HTTP 404

This route does not exist by design. Account deletion can span the consumer and
Access databases, so the generic SDK does not choose the order or failure
policy.

The consumer must expose its own authorized account-deletion operation, delete
or retain its data according to product policy, and call
`IBullgateAccessClient.DeleteAccountAsync` at the chosen point. Retries,
partial failure, audit, and user-visible status belong to the consumer.

## A structured error loses its original meaning

The BFF preserves structured Access rejections only for recognized domain
statuses and error bodies. An empty, malformed, or unrecognized error response
becomes `access-unavailable` because treating unknown data as a business
decision would invent a contract.

Consumer provisioning rejections should use
`BullgateApplicationRejectedException` with a safe machine-readable error,
appropriate HTTP status, and optional public field. Never include secrets or
personal data in the error code or field.

## Information to collect for a safe bug report

Include:

- package version or commit;
- route and HTTP method;
- HTTP status and safe machine-readable error;
- whether the failure occurs before or after a cookie is written;
- session purpose, without token or personal data;
- consumer adapter involved;
- operating system, .NET SDK, and hosting model;
- minimal reproduction with synthetic data.

Exclude credentials, cookies, tokens, OTPs, provider assertions, real email
addresses, phone numbers, and complete upstream response bodies.

See the [Access integration guide](access-integration.md), the
[Access BFF HTTP API](access-http-api.md), and the
[security model](security-model.md) for the underlying contracts.
