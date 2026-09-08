# Access controlled glossary

Use these meanings consistently in Access documentation, code comments,
retrieval metadata, and assistant answers.

| Term | Meaning | Do not confuse with |
| --- | --- | --- |
| Access | The separately operated Bullgate identity and access service. | This SDK, the entire Bullgate platform, or a consumer product. |
| Access SDK package | `Bullgate.Access.AspNetCore`, the server-side adapter that runs inside a consumer ASP.NET Core application. | A hosted Access service or client-side authentication library. |
| Consumer | The product backend that embeds the SDK and owns its local profile, authorization, and application policy. | The end user or Bullgate Access. |
| BFF | Backend for Frontend routes mapped by the consumer under `/bullgate/access/v1`. | Bullgate Access itself or a standalone SDK process. |
| Typed Access client | `IBullgateAccessClient`, the trusted server-to-server transport boundary. | A safe client for browser or mobile use. |
| Integration credential | Opaque `bgic_` server bearer credential used for outbound Access calls. | A public application-client key, session token, or flow capability. |
| Application-client key | Stable public selector for one client definition inside the credential-selected environment. | A secret, database UUID, package name, or tenancy selector. |
| Bullgate identity | Identity record owned by Access and identified by `IdentityId`. | Consumer product profile or ASP.NET Core subject. |
| Consumer profile | Product-owned `TApplication` value provisioned or resolved by the consumer adapter. | Bullgate identity or session. |
| Local subject | Consumer-owned identifier returned by `IBullgatePrincipalResolver` as the ASP.NET Core name identifier. | Bullgate `IdentityId`. |
| Session token | Opaque Access bearer used for introspection and account operations. | Session id, cookie name, or consumer authentication cookie payload. |
| Session cookie | Adapter-owned host-only `HttpOnly` cookie that stores the opaque session token. | A JSON session envelope or flow capability cookie. |
| Session id | Stable identifier optionally returned for a Bullgate session and exposed as a claim when present. | The bearer session token. |
| Registration-purpose session | Valid Access session that may continue registration but cannot authenticate product endpoints. | Product-purpose session or incomplete transport response. |
| Product-purpose session | Active Access session eligible for consumer-profile and local-principal resolution. | Proof that a consumer profile exists. |
| Session envelope | Public BFF representation of identity facts, session state, and an optional consumer profile. | The opaque bearer session token. |
| Session feature | `IBullgateSessionFeature`, request-local access to an introspected Bullgate session, including registration-purpose state. | An authenticated ASP.NET Core principal. |
| Principal resolver | Consumer implementation that maps a product-purpose Bullgate session to trusted local subject and claims. | Product-profile provisioner or Access introspection. |
| Access application adapter | Consumer implementation of `IBullgateAccessApplication<TRegistration,TApplication>`. | A UI application or Bullgate application client. |
| Provisioning | Idempotent consumer-owned creation or retrieval of a product profile from validated registration data. | Identity creation inside Access. |
| AccessFlow | Versioned multi-step protocol exposed through snapshots and currently advertised actions. | A consumer screen sequence or client-owned state machine. |
| Flow id | Stable UUID that selects an AccessFlow. | Authorization to read or act on the flow. |
| Flow capability | Temporary bearer authority for one flow, stored in the host-only flow cookie. | Flow id, action id, or application-client key. |
| Flow cookie | Adapter-owned host-only `HttpOnly` cookie scoped to `/bullgate/access/v1` that stores the capability. | Session cookie or public flow snapshot. |
| Flow snapshot | Public semantic state containing protocol version, revision, intent, status, step, actions, feedback, and result. | Capability bearer or server persistence model. |
| Request id | Caller-provided idempotency identifier for a flow start or action. | Flow id, action id, or expected revision. |
| Expected revision | Optimistic-concurrency revision the caller believes it is acting on. | Package, route, or protocol version. |
| Structured Access rejection | Upstream business/protocol rejection represented by `BullgateAccessRejectedException`. | Transport failure or consumer-profile rejection. |
| Consumer rejection | Product-level rejection represented by `BullgateApplicationRejectedException`. | Access domain rejection or HTTP infrastructure failure. |
| Access unavailable | Timeout, transport failure, or invalid/unusable upstream response represented by `BullgateAccessUnavailableException`. | Invalid credentials, inactive session, or every HTTP 5xx response by definition. |
| Public configuration projection | Public environment and application-client metadata returned for a valid public key. | Complete protected configuration or authorization material. |
| Online introspection | Per-request validation of a presented session against Access. | Local JWT validation or proof that the consumer profile exists. |
| Account deletion | Removal requested for the current Access identity through trusted session context. | Atomic deletion of consumer-owned data. |
| Public source repository | Source and documentation visible to the public. | Published NuGet artifact, deployed service, or support commitment. |
| Published package | Immutable version available from an identified package registry. | A project version in a `.csproj` or a CI package artifact. |
| Hosted runtime | Separately deployed Bullgate Access service identified by `BaseAddress`. | The SDK assembly embedded in a consumer backend. |
