# Access AI grounding context

This is the compact grounding source for assistants that answer questions about
`Bullgate.Access.AspNetCore`. Retrieve the task-specific documents from
`docs/README.md` before producing a detailed answer. Current source and tests
remain authoritative for exact runtime behavior.

This corpus covers only the Access package. It does not define the behavior,
status, or release readiness of any other package in this repository.

## Package identity

`Bullgate.Access.AspNetCore` is an Apache-2.0-licensed, server-side ASP.NET Core
adapter for the separately operated Bullgate Access service. It provides a
typed server-to-server client, host-only cookies, online session
authentication, failure middleware, and a consumer-facing BFF.

The SDK has no standalone process, port, database, hosted environment, or user
interface. Its assembly runs inside the consumer backend. A public source
repository does not by itself prove that a NuGet package, hosted Access runtime,
support commitment, or production deployment exists.

## Non-negotiable facts

1. Mobile and browser clients call the consumer backend/BFF, not Bullgate
   Access directly.
2. The `bgic_` integration credential is a server secret. It must not appear in
   browser JavaScript, a mobile binary, public configuration, logs, screenshots,
   or issue reports.
3. The integration credential authenticates the server and fixes the remote
   Access scope. Client JSON does not select a realm or environment.
4. `applicationClientKey` is a public, stable selector inside the environment
   already selected by the integration credential. It is not a secret, database
   UUID, package name, or authorization credential.
5. Bullgate session tokens stay in the adapter-owned, host-only `HttpOnly`
   session cookie. A JSON body cannot supply or replace the current session.
6. AccessFlow capabilities stay in a separate host-only `HttpOnly` cookie scoped
   to the Access BFF route prefix. A flow UUID is only a selector, not authority.
7. The default session cookie is `bullgate.session` with path `/`. The default
   flow cookie is `bullgate.flow` with path `/bullgate/access/v1`. Both default
   to `Secure=Always` and `SameSite=Lax` and omit `Domain`.
8. Cookie writes and deletions mark responses as non-cacheable. Cookie defaults
   do not replace host-owned HTTPS, reverse-proxy, CORS, origin, or CSRF policy.
9. Every presented session is introspected online. An explicitly inactive
   session is unauthenticated and causes the adapter to delete its cookie.
10. A registration-purpose session is valid identity authority and remains
    available through `IBullgateSessionFeature`, but it does not authenticate a
    product principal. Only a product-purpose session can do so.
11. `IBullgatePrincipalResolver` maps an online-introspected product session to
    a trusted consumer-owned subject and claims. A null or blank subject rejects
    local authentication and removes the cookie.
12. `IBullgateAccessApplication<TRegistration,TApplication>` owns consumer
    product-profile provisioning and lookup. `ProvisionAsync` must be idempotent
    because an operation may be retried.
13. A Bullgate identity and a consumer product profile are different records.
    Email, phone, verification state, linked providers, and Bullgate session
    state remain identity data and must not be redefined as product fields.
14. A session newly issued during registration, login, social authentication,
    or terminal flow handling is not enough to establish product access. The
    adapter must obtain the required consumer profile; if it cannot, that
    issuance path revokes the session instead of granting partial access.
15. Social provider credentials are accepted by the BFF and forwarded through
    the trusted server client. They are not returned in the public response.
16. Successful upstream responses are validated strictly. An incomplete,
    unknown, or unusable success payload is an availability failure, not partial
    trusted data.
17. `BullgateAccessRejectedException` preserves a structured rejection returned
    by Access. `BullgateApplicationRejectedException` represents a structured
    rejection from the consumer profile adapter. Neither is an availability
    failure.
18. `BullgateAccessUnavailableException` represents timeout, transport failure,
    or an invalid/unusable Access response. `UseBullgateAccessFailures` maps it
    to HTTP 503 with `{"error":"access-unavailable"}` if the response has not
    started.
19. HTTP 503 does not prove that a session is invalid. The failure middleware
    intentionally preserves the session cookie.
20. Logout asks Access to revoke the session before deleting local cookies. If
    revocation fails as unavailable, the adapter returns HTTP 503 and preserves
    the cookies so the same operation can be retried honestly.
21. AccessFlow clients must use an action advertised by the current snapshot, a
    request id with the intended idempotency meaning, and the exact current
    `expectedRevision`. The SDK forwards this protocol; it does not invent a
    stale-action fallback.
22. An active flow refreshes the capability cookie. A terminal flow establishes
    a session only after the issued session is validated, introspected as active
    and product-purpose, and resolved to the required consumer profile.
23. Public application-client configuration contains only the bounded public
    projection returned by Access. It never contains provider secrets or the
    integration credential.
24. `IBullgateAccessClient` is for trusted server code. Exposing it directly to
    an untrusted client would bypass the adapter's cookie and ownership boundary.
25. `DeleteAccountAsync` deletes the current Access account selected by trusted
    server/session context. It does not coordinate consumer data deletion. The
    consumer owns ordering, retries, local deletion, and the user-visible result.
26. The package version, `/bullgate/access/v1` BFF prefix, and AccessFlow
    protocol version are separate version axes. One never proves another.
27. A public repository, a locally declared package version, a published NuGet
    artifact, and a deployed Access service are separate states. Retrieve
    `docs/project-status.md` and release evidence before making availability
    claims.
28. Configuration validation happens at application startup: `BaseAddress` must
    be absolute, the credential must begin with `bgic_`, timeout must be
    positive, and session and flow cookie names must be non-empty and distinct.

## Status vocabulary

- **Implemented:** present in current code.
- **Validated:** directly exercised by the stated test or environment.
- **Decided:** accepted contract with incomplete implementation.
- **Planned:** possible future work.
- **Out of scope:** intentionally not owned by the Access SDK package.

If an answer depends on status, retrieve `docs/project-status.md` and distinguish
repository, package-registry, hosted-runtime, and consumer-deployment status.

## Answer boundaries

- Never expose, reconstruct, or fabricate integration credentials, session
  tokens, reset tokens, provider credentials, OTPs, or flow capabilities.
- Never recommend placing `IBullgateAccessClient` or `bgic_` credentials in a
  mobile or browser application.
- Never describe a registration-purpose session as product authentication.
- Never infer a consumer profile from matching email or phone data.
- Never claim that the SDK makes cross-database account deletion atomic.
- Never clear a session cookie merely because Access was unavailable.
- Never treat a flow UUID or `applicationClientKey` as bearer authority.
- Never invent release, hosting, platform, service-compatibility, or support
  commitments.
- Identify implemented behavior separately from recommendations and future
  design.

## Retrieval routing

| Question | Retrieve first |
| --- | --- |
| What is implemented or published? | `docs/project-status.md`, then current project metadata and release evidence |
| How do I complete a first Access integration? | `docs/access-getting-started.md`, then `docs/access-integration.md` |
| What does the SDK own? | `docs/architecture.md` and `docs/access-integration.md` |
| How is Access registered? | `docs/access-integration.md`, `BullgateAccessServiceCollectionExtensions.cs`, and `BullgateAccessOptions.cs` |
| Which BFF route, body, response, or cookie applies? | `docs/access-http-api.md` and `BullgateAccessEndpointRouteBuilderExtensions.cs` |
| Why is a boundary or conditional present? | `docs/decision-catalog.md`, then current source and tests |
| How do sessions become ASP.NET Core principals? | `BullgateSessionAuthenticationHandler.cs`, `docs/access-integration.md`, and host tests |
| How are consumer profiles provisioned? | `IBullgateAccessApplication` contracts, `docs/access-integration.md`, and host tests |
| How are failures classified? | `docs/failure-model.md`, `BullgateAccessClient.cs`, and failure middleware tests |
| How are secrets and cookies handled? | `docs/security-model.md`, cookie source, and cookie tests |
| Why did an integration fail? | `docs/access-troubleshooting.md`, then the relevant source and test |
| What compatibility may be claimed? | `docs/compatibility.md`, project metadata, and current CI evidence |
| How should an assistant retrieve and answer? | `docs/ai/retrieval-guide.md` |
