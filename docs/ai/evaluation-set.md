# Access baseline AI evaluation set

Use these cases to evaluate retrieval and answers about
`Bullgate.Access.AspNetCore`. Each case defines facts that must appear, claims
that must not appear, and the primary evidence to retrieve.

The set is intentionally limited to the Access SDK package. Run it against a
known repository revision and record that revision with the result.

## AI-DOTNET-ACCESS-001 — Package identity

**Question:** What is `Bullgate.Access.AspNetCore`?

**Required answer facts:**

- it is a server-side ASP.NET Core adapter for Bullgate Access;
- it provides a typed client, cookies, authentication, failure middleware, and
  BFF routes;
- it runs inside the consumer backend and has no standalone runtime.

**Prohibited claims:** it is the Access service, a mobile SDK, a database, or a
hosted identity product.

**Primary evidence:** `docs/architecture.md`, `docs/access-integration.md`, and
the package README.

## AI-DOTNET-ACCESS-002 — Client topology

**Question:** Should a mobile or browser application call Bullgate Access
directly with the .NET integration credential?

**Required answer facts:**

- no; the untrusted client calls the consumer BFF;
- only trusted server code uses `IBullgateAccessClient` and the `bgic_`
  credential.

**Prohibited claims:** embedding the credential in mobile configuration,
JavaScript, or a public environment variable is acceptable.

**Primary evidence:** `docs/access-integration.md` and
`BullgateAccessServiceCollectionExtensions.cs`.

## AI-DOTNET-ACCESS-003 — Integration scope

**Question:** Can a BFF request choose the Access realm or environment in JSON?

**Required answer facts:**

- no; trusted scope comes from the server integration credential;
- client data cannot replace or elevate that scope.

**Prohibited claims:** accepting a caller-provided realm or environment is a
normal multi-tenant pattern for this adapter.

**Primary evidence:** `docs/architecture.md`, `docs/access-http-api.md`, and
current client source.

## AI-DOTNET-ACCESS-004 — Application-client key

**Question:** Is `applicationClientKey` secret authentication material?

**Required answer facts:**

- it is public and stable;
- it selects public client metadata inside the credential-selected environment;
- it is not a database UUID or authorization credential.

**Prohibited claims:** it can replace `IntegrationCredential` or select an
arbitrary tenant.

**Primary evidence:** `docs/access-http-api.md` under public application-client
configuration and the configuration client tests.

## AI-DOTNET-ACCESS-005 — Session bearer location

**Question:** Where does the adapter store the Bullgate session token?

**Required answer facts:**

- in the host-only `HttpOnly` session cookie;
- the default name is `bullgate.session` and path is `/`;
- public JSON does not contain the token.

**Prohibited claims:** local storage, a readable JavaScript cookie, or the
session envelope is the bearer.

**Primary evidence:** `BullgateSessionCookie.cs`, cookie tests, and
`docs/access-http-api.md`.

## AI-DOTNET-ACCESS-006 — Flow authority

**Question:** Is a flow UUID enough to read or act on an AccessFlow?

**Required answer facts:**

- no; the UUID is a selector;
- the matching bearer capability is stored in the host-only flow cookie;
- the capability is never returned in public JSON.

**Prohibited claims:** putting the flow id in the route authenticates the flow.

**Primary evidence:** flow sections in `docs/access-http-api.md`,
`BullgateFlowCookie.cs`, and flow host tests.

## AI-DOTNET-ACCESS-007 — Cookie separation

**Question:** Why are there separate session and flow cookies?

**Required answer facts:**

- product-session and temporary flow authority have different scope and
  lifetime;
- the default flow cookie is `bullgate.flow` scoped to
  `/bullgate/access/v1`;
- option validation requires distinct names.

**Prohibited claims:** both cookies are interchangeable or should share one
name.

**Primary evidence:** cookie implementations, option validation, and cookie
tests.

## AI-DOTNET-ACCESS-008 — Registration session

**Question:** Does a valid registration-purpose session authenticate protected
product routes?

**Required answer facts:**

- no;
- it remains visible through `IBullgateSessionFeature` and the session envelope;
- only product-purpose sessions can proceed to local-principal resolution.

**Prohibited claims:** possession of any valid session proves product
registration is complete.

**Primary evidence:** `BullgateSessionAuthenticationHandler.cs`,
`docs/access-integration.md`, and registration-session host tests.

## AI-DOTNET-ACCESS-009 — Online introspection

**Question:** Does the authentication handler trust a locally present cookie
without contacting Access?

**Required answer facts:**

- no; it introspects every presented session online;
- no cookie means no Access call;
- explicit inactive state removes the cookie.

**Prohibited claims:** the cookie is a locally verified JWT or permanently valid
until browser expiration.

**Primary evidence:** `BullgateSessionAuthenticationHandler.cs` and session host
tests.

## AI-DOTNET-ACCESS-010 — Consumer principal

**Question:** Who decides the ASP.NET Core subject and additional product
claims?

**Required answer facts:**

- the consumer implementation of `IBullgatePrincipalResolver`;
- values must come from trusted local state;
- null or blank subject rejects local authentication.

**Prohibited claims:** the SDK invents a local profile id or trusts one supplied
by the browser.

**Primary evidence:** `BullgateAccessContracts.cs`, authentication handler, and
authentication host tests.

## AI-DOTNET-ACCESS-011 — Profile ownership

**Question:** Should the consumer copy email, phone, and provider state into
`TApplication` because the SDK requires it?

**Required answer facts:**

- no; those are Access-owned identity facts already present in the session
  envelope;
- `TApplication` is the consumer-owned product profile.

**Prohibited claims:** duplicate identity data is required for authentication
or becomes authoritative over Access.

**Primary evidence:** `docs/access-integration.md` and session/application
contracts.

## AI-DOTNET-ACCESS-012 — Idempotent provisioning

**Question:** Why must `ProvisionAsync` be idempotent?

**Required answer facts:**

- registration completion may be retried after an ambiguous delivery;
- repeated calls must not create duplicate product profiles;
- the consumer owns the transaction and implementation.

**Prohibited claims:** the SDK makes the consumer database transaction
idempotent automatically.

**Primary evidence:** `IBullgateAccessApplication` XML documentation,
`docs/access-integration.md`, and registration host tests.

## AI-DOTNET-ACCESS-013 — Missing product profile

**Question:** What happens if Access issues a product session but the consumer
cannot resolve or provision its required profile?

**Required answer facts:**

- the adapter does not grant partial product access;
- it revokes the issued session;
- the relevant BFF operation returns its documented explicit failure, such as
  `application-not-provisioned` where applicable.

**Prohibited claims:** the adapter returns an authenticated session with a null
profile or silently invents one.

**Primary evidence:** registration, login, social, and terminal-flow host tests;
`docs/access-http-api.md`.

## AI-DOTNET-ACCESS-014 — Public configuration

**Question:** Does the public application-client configuration endpoint expose
provider secrets?

**Required answer facts:**

- no; it returns only the bounded public projection;
- selection occurs inside the integration-credential scope;
- the application-client key is public.

**Prohibited claims:** the response contains integration credentials or the
complete protected environment configuration.

**Primary evidence:** public-configuration section in
`docs/access-http-api.md`, contracts, and host tests.

## AI-DOTNET-ACCESS-015 — Social credential handling

**Question:** Can Google or Apple credential material be returned to the app in
the BFF response for reuse?

**Required answer facts:**

- no; it is operation input forwarded through the trusted server boundary;
- the response contains only the documented session envelope or error.

**Prohibited claims:** provider credentials are application state or safe to
log.

**Primary evidence:** social endpoint source, `docs/access-http-api.md`, and
social host tests.

## AI-DOTNET-ACCESS-016 — Invalid success payload

**Question:** Access returned HTTP success but omitted required session data.
Should the SDK accept the remaining fields?

**Required answer facts:**

- no; successful payloads are validated strictly;
- incomplete or unusable success becomes Access unavailable;
- partial security state is never trusted.

**Prohibited claims:** filling missing values with defaults or returning a
partial authenticated result is safe.

**Primary evidence:** `BullgateAccessClient.cs`, failure model, and invalid
success-payload tests.

## AI-DOTNET-ACCESS-017 — Failure classification

**Question:** Are all Access failures represented by one exception?

**Required answer facts:**

- no; structured Access rejection, consumer rejection, and Access unavailability
  are distinct;
- status/error/field are preserved for valid structured rejections;
- availability covers timeout, transport, and unusable upstream responses.

**Prohibited claims:** every failure should become HTTP 500 or every rejection
is safe to retry.

**Primary evidence:** `docs/failure-model.md`, exception source, and client
tests.

## AI-DOTNET-ACCESS-018 — HTTP 503 and cookies

**Question:** Should the adapter delete the session cookie when Access is
temporarily unavailable?

**Required answer facts:**

- no; unavailability does not prove invalidity;
- the middleware returns no-store HTTP 503 with `access-unavailable` when the
  response has not started;
- the cookie is preserved.

**Prohibited claims:** clearing the cookie is a safe recovery or that HTTP 503
means the user is logged out.

**Primary evidence:** failure middleware, failure model, troubleshooting guide,
and host test.

## AI-DOTNET-ACCESS-019 — HTTP 401 versus 403

**Question:** What is the difference between HTTP 401 and 403 on a protected
consumer route?

**Required answer facts:**

- 401 means no usable authenticated product principal was established;
- 403 means authentication succeeded but consumer authorization denied access;
- registration-purpose state does not authenticate a product route.

**Prohibited claims:** 403 necessarily means Access rejected the credential or
401 proves the account does not exist.

**Primary evidence:** authentication handler and the 401/403 sections in
`docs/access-troubleshooting.md`.

## AI-DOTNET-ACCESS-020 — Logout outage

**Question:** Why might logout return HTTP 503 without clearing cookies?

**Required answer facts:**

- remote revocation is attempted before local cookie deletion;
- an availability failure does not prove revocation;
- preserving cookies permits an honest retry.

**Prohibited claims:** the user is definitely logged out or deleting locally is
equivalent to remote revocation.

**Primary evidence:** logout endpoint, host test, and troubleshooting guide.

## AI-DOTNET-ACCESS-021 — Account deletion boundary

**Question:** Does `DeleteAccountAsync` atomically delete Access identity data
and the consumer's product database?

**Required answer facts:**

- no; it requests deletion of the current Access account;
- the consumer owns local deletion, order, retry policy, and user-visible result;
- there is no distributed transaction across the databases.

**Prohibited claims:** the SDK guarantees cross-database atomicity or chooses a
universal deletion order.

**Primary evidence:** account deletion section in `docs/access-integration.md`,
account endpoint tests, and architecture.

## AI-DOTNET-ACCESS-022 — Current identity selection

**Question:** Can an account-management request submit a session token or
identity id in JSON to select another account?

**Required answer facts:**

- no; the current session is read exclusively from the host-only cookie;
- identity operations act within trusted server and session context.

**Prohibited claims:** body-selected identity is an administrative feature of
the SDK.

**Primary evidence:** account-management sections in `docs/access-http-api.md`
and current-identity host tests.

## AI-DOTNET-ACCESS-023 — Flow concurrency

**Question:** What should a client send when acting on the current AccessFlow
snapshot?

**Required answer facts:**

- an action currently advertised by the snapshot;
- a request id with the intended idempotency meaning;
- the exact current `expectedRevision`;
- the matching capability remains in the cookie.

**Prohibited claims:** guessing an action, incrementing the revision locally, or
using the flow id as authority.

**Primary evidence:** flow action section in `docs/access-http-api.md`, flow
contracts, and flow tests.

## AI-DOTNET-ACCESS-024 — Terminal flow session

**Question:** Does every completed AccessFlow automatically become a logged-in
product session?

**Required answer facts:**

- no; the terminal result must include an issued product-purpose session;
- the adapter introspects it as active and product-purpose;
- the consumer profile must resolve before the session cookie is established.

**Prohibited claims:** completed flow status alone authenticates the product.

**Primary evidence:** terminal-flow section in `docs/access-http-api.md`, host
tests, and decision `DOTNET-ACCESS-017`.

## AI-DOTNET-ACCESS-025 — Startup validation

**Question:** When are invalid Access options rejected?

**Required answer facts:**

- options use startup validation;
- base address must be absolute, the credential must begin with `bgic_`, timeout
  must be positive, and cookie names must be non-empty and distinct.

**Prohibited claims:** these values are first validated only after an end-user
request.

**Primary evidence:** `BullgateAccessServiceCollectionExtensions.cs` and startup
troubleshooting sections.

## AI-DOTNET-ACCESS-026 — Caller cancellation

**Question:** Is caller-requested cancellation the same as an Access timeout?

**Required answer facts:**

- no; caller cancellation remains cancellation;
- adapter timeout or transport failure becomes Access unavailable.

**Prohibited claims:** every `OperationCanceledException` proves Access was
unavailable.

**Primary evidence:** `BullgateAccessClient.cs`, `docs/failure-model.md`, and
timeout/cancellation client tests.

## AI-DOTNET-ACCESS-027 — CSRF ownership

**Question:** Do the SDK's `HttpOnly`, `Secure`, and `SameSite=Lax` defaults
fully solve CSRF and cross-origin security?

**Required answer facts:**

- no; they are safety defaults, not a complete host policy;
- the consumer owns HTTPS, proxies, CORS, origins, credential mode, and CSRF
  controls.

**Prohibited claims:** no host configuration is required because cookies are
`HttpOnly`.

**Primary evidence:** `docs/security-model.md`, cookie defaults in
`docs/access-http-api.md`, and cookie source.

## AI-DOTNET-ACCESS-028 — Version axes

**Question:** Does `/bullgate/access/v1` mean the NuGet package is version
1.0.0?

**Required answer facts:**

- no; BFF route version, AccessFlow protocol version, and package version are
  independent;
- retrieve project and package metadata for the actual package version.

**Prohibited claims:** route or protocol version establishes binary package
stability.

**Primary evidence:** `BullgateAccessDefaults.cs`, project metadata, and
`docs/compatibility.md`.

## AI-DOTNET-ACCESS-029 — Public repository versus NuGet

**Question:** The source repository is public. Does that prove the package can
be restored from NuGet?

**Required answer facts:**

- no; source visibility and registry publication are separate states;
- a local project version or CI artifact is not a public package release;
- the current project-status document says no versioned package has been
  published to a public registry;
- retrieve `docs/project-status.md` and exact registry/release evidence.

**Prohibited claims:** a public Git repository automatically creates a NuGet
package or support commitment.

**Primary evidence:** project status, packaging guide, and release process.

## AI-DOTNET-ACCESS-030 — Package versus hosted runtime

**Question:** If the SDK package is available, does that mean Bullgate Access is
deployed and reachable?

**Required answer facts:**

- no; the SDK is embedded in a consumer backend;
- `BaseAddress` points to a separately operated Access service;
- package and deployment evidence are independent.

**Prohibited claims:** installing the assembly starts Access or provisions a
database.

**Primary evidence:** architecture, project status, and Access options.

## AI-DOTNET-ACCESS-031 — Generic type compatibility

**Question:** Does package compatibility guarantee compatibility with every
consumer's `TRegistration` and `TApplication` schema?

**Required answer facts:**

- no; those shapes and persistence rules belong to the consumer;
- the generic types must be used consistently during service registration and
  endpoint mapping;
- consumer schema changes may break that integration independently.

**Prohibited claims:** the SDK owns or migrates the consumer database schema.

**Primary evidence:** contracts, integration guide, compatibility guide, and
service-resolution tests.

## AI-DOTNET-ACCESS-032 — Direct client use

**Question:** When is it appropriate to use `IBullgateAccessClient` directly?

**Required answer facts:**

- only from trusted server code;
- it exposes typed Access operations while preserving the integration
  credential boundary;
- browser and mobile applications use the BFF instead.

**Prohibited claims:** registering the client in a WebAssembly or mobile
dependency container is supported.

**Primary evidence:** direct-client section in `docs/access-integration.md` and
client registration source.

## AI-DOTNET-ACCESS-033 — Secure cookies in local development

**Question:** Why is the browser not returning the session cookie over local
plain HTTP?

**Required answer facts:**

- the default is `CookieSecurePolicy.Always`;
- secure cookies are not returned over plain HTTP;
- deployed environments should use HTTPS, and any local override must be
  explicit and limited to the intended environment.

**Prohibited claims:** disable cookie security globally or expose the token in
JavaScript as the normal fix.

**Primary evidence:** cookie source and the cookie-storage section in
`docs/access-troubleshooting.md`.

## AI-DOTNET-ACCESS-034 — Safe diagnostics

**Question:** What information may be collected for an Access SDK bug report?

**Required answer facts:**

- route and method, safe error code/status, relative timing, cookie side-effect
  stage, non-secret session purpose, package/runtime versions, and synthetic
  reproduction data are useful;
- credentials, cookies, tokens, OTPs, provider assertions, and real personal
  data must be excluded.

**Prohibited claims:** paste a token, secret configuration, or real identity
data to make the report reproducible.

**Primary evidence:** safe bug-report section in
`docs/access-troubleshooting.md` and security model.

## AI-DOTNET-ACCESS-035 — Status vocabulary

**Question:** What does "validated" mean in this documentation?

**Required answer facts:**

- it means directly exercised by the stated test or named environment;
- it does not mean merely implemented or planned;
- the answer must name the actual evidence boundary.

**Prohibited claims:** validated means production-ready, publicly deployed, or
supported on every platform.

**Primary evidence:** status language in `docs/README.md` and
`docs/ai/context.md`.

## AI-DOTNET-ACCESS-036 — Whole-platform chatbot routing

**Question:** A user asks why Access rejected an OTP. Can the .NET SDK corpus
explain the service's proof-authority and persistence rules by itself?

**Required answer facts:**

- the SDK corpus can explain transport, public errors, cookies, and adapter
  handling;
- service-domain and persistence rules require the matching Access service
  corpus and compatible revision;
- the final answer should identify each component's ownership.

**Prohibited claims:** infer undocumented service internals from an SDK
exception or merge facts from unrelated revisions silently.

**Primary evidence:** `docs/ai/retrieval-guide.md`, architecture, and the
relevant service repository when available.

## Evaluation procedure

For every case:

1. record repository revision, corpus build, retrieval configuration, and model;
2. capture retrieved chunks before judging the answer;
3. mark each required fact as present or absent;
4. fail the case if any prohibited claim appears;
5. record unsupported extra claims separately from style defects;
6. update the case when an approved answer-visible contract changes.

Passing this set measures grounding against the recorded revision. It does not
authorize production changes, publish a package, validate a hosted service, or
replace security review.
