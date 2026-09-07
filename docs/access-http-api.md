# Access BFF HTTP API

This document describes the HTTP surface mapped by
`MapBullgateAccess<TRegistration,TApplication>()`. It covers the adapter
contract exposed by a consumer ASP.NET Core application. It is not the
server-to-server API of the Bullgate Access service.

All routes use the prefix:

```text
/bullgate/access/v1
```

ASP.NET Core serializes the SDK records as camel-case JSON. The examples below
use placeholders for the consumer-defined `TRegistration` and `TApplication`
types.

## Route summary

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/register` | Register by email and password and provision the product profile. |
| `POST` | `/login` | Authenticate by email and password. |
| `POST` | `/google` | Authenticate or register with Google. |
| `POST` | `/apple` | Authenticate or register with Apple. |
| `GET` | `/session` | Resolve the current product session. |
| `POST` | `/logout` | Revoke the current session and delete adapter cookies. |
| `GET` | `/config/application-clients/{applicationClientKey}` | Read public client configuration. |
| `POST` | `/password/recovery/email` | Request password recovery by email. |
| `POST` | `/password/recovery/phone` | Request a phone recovery code. |
| `POST` | `/password/recovery/phone/confirm` | Confirm a phone recovery code. |
| `POST` | `/password/recovery/reset` | Set a password using a recovery token. |
| `POST` | `/account/password` | Change the current identity password. |
| `PUT` | `/account/email` | Change the current identity email. |
| `POST` | `/account/google` | Link Google to the current identity. |
| `DELETE` | `/account/google` | Unlink Google from the current identity. |
| `POST` | `/account/apple` | Link Apple to the current identity. |
| `DELETE` | `/account/apple` | Unlink Apple from the current identity. |
| `POST` | `/flows` | Start an AccessFlow. |
| `GET` | `/flows/{flowId:guid}` | Read the current AccessFlow revision. |
| `POST` | `/flows/{flowId:guid}/actions` | Apply an idempotent action to an AccessFlow. |

## Trust and authentication

The BFF receives end-user credentials and provider tokens, then sends them to
Access using the server-only integration credential. The integration credential
is never accepted from or returned to the application client.

The adapter stores the opaque Access session token in an `HttpOnly`, host-only
cookie. Authenticated account routes read that cookie; the JSON body cannot
replace or select the session. AccessFlow capabilities use a separate
`HttpOnly`, host-only cookie scoped to the BFF route prefix.

The host remains responsible for HTTPS, proxy configuration, CORS, CSRF
protection, request limits, rate limits, and any authorization outside the
identity operations defined here.

## Session response

Authentication, registration, session, and account-management operations return
this envelope on success:

```json
{
  "state": "authenticated",
  "identityId": "4ce297fa-b06d-4af0-8bed-cf6a032d846a",
  "email": "person@example.test",
  "phone": "+15555550100",
  "phoneVerifiedAt": "2026-09-07T12:00:00Z",
  "sessionExpiresAt": "2026-09-08T12:00:00Z",
  "hasPassword": true,
  "hasGoogle": false,
  "googleEmail": null,
  "hasApple": false,
  "appleEmail": null,
  "application": {
    "id": "consumer-defined",
    "name": "Consumer-defined profile"
  }
}
```

`state` is `authenticated` for a product-purpose session and
`registration` for a registration-purpose session. `application` is the
consumer-defined `TApplication` value for an authenticated product session
and null during registration.

Identity fields belong to Access. The `application` object belongs to the
consumer application. Consumers should not duplicate email, phone,
verification state, or linked provider state inside `TApplication`.

## Register by email and password

```http
POST /bullgate/access/v1/register
Content-Type: application/json
```

```json
{
  "email": "person@example.test",
  "password": "user-supplied-password",
  "application": {
    "name": "Consumer-defined registration data"
  }
}
```

`application` is required and must match `TRegistration`. The adapter
registers the identity, then calls
`IBullgateAccessApplication<TRegistration,TApplication>.ProvisionAsync`.

A retry that receives `email-taken` attempts authentication with the supplied
credentials so it can resume product provisioning after a partially completed
earlier request. Consumer provisioning must therefore be idempotent.

Success returns HTTP 200 with the session envelope and writes the session
cookie. A missing application payload returns HTTP 400:

```json
{
  "error": "missing-fields",
  "field": "application"
}
```

If consumer provisioning rejects the request, its status, error, and field are
returned and the newly issued Access session is revoked.

## Login by email and password

```http
POST /bullgate/access/v1/login
Content-Type: application/json
```

```json
{
  "email": "person@example.test",
  "password": "user-supplied-password"
}
```

Success returns HTTP 200 with the session envelope and writes the session
cookie. Access authentication alone does not create a product profile. If the
consumer cannot resolve one, the adapter revokes the issued session and returns
HTTP 409:

```json
{
  "error": "application-not-provisioned",
  "field": null
}
```

## Google authentication

```http
POST /bullgate/access/v1/google
Content-Type: application/json
```

```json
{
  "idToken": "provider-issued-id-token",
  "accessToken": "provider-issued-access-token",
  "application": {
    "name": "Required only when product provisioning is needed"
  }
}
```

The adapter forwards the provider tokens to Access. If the local product profile
already exists, `application` may be null. If no profile exists, the adapter
requires `application` and provisions it idempotently.

Success returns HTTP 200 with the session envelope and writes the session
cookie. A missing application payload when provisioning is required returns
HTTP 400 with `missing-fields` and field `application`. The adapter revokes a
newly issued session when it cannot establish the required product profile.

## Apple authentication

```http
POST /bullgate/access/v1/apple
Content-Type: application/json
```

```json
{
  "identityToken": "provider-issued-identity-token",
  "application": {
    "name": "Required only when product provisioning is needed"
  }
}
```

The profile resolution, provisioning, success response, cookie behavior, and
failure behavior are the same as for Google authentication.

## Read the current session

```http
GET /bullgate/access/v1/session
```

The Bullgate authentication handler introspects the session cookie before the
endpoint runs. Success returns HTTP 200 with the current session envelope and
`Cache-Control: no-store`.

The endpoint returns HTTP 401 when no active session exists or the consumer
cannot resolve the product profile. A registration-purpose session does not
authenticate a product principal.

## Logout

```http
POST /bullgate/access/v1/logout
```

When a session cookie exists, the adapter asks Access to revoke it. It then
deletes both the session and flow cookies and returns HTTP 204.

If Access is unavailable before revocation completes, the failure middleware
returns HTTP 503 and preserves the cookies because unavailability does not prove
that the session is invalid.

## Read public application-client configuration

```http
GET /bullgate/access/v1/config/application-clients/android-play
```

Success returns HTTP 200:

```json
{
  "configurationVersion": 1,
  "common": {
    "supportUrl": "https://example.test/support"
  },
  "client": {
    "key": "android-play",
    "name": "Android Play",
    "platform": "android",
    "applicationId": "app.example.test",
    "signingIdentity": "sha256:public-signing-identity",
    "smsRetrieverAppHash": "AbCdEfGhIJK",
    "configuration": {}
  }
}
```

The key is public, stable, and scoped to the environment authenticated by the
server integration credential. It is not a database UUID. Empty or whitespace
keys return HTTP 400 with `application-client-invalid` and field
`applicationClientKey`.

Only public configuration is returned. Provider secrets and integration
credentials are never part of this document.

## Password recovery by email

```http
POST /bullgate/access/v1/password/recovery/email
Content-Type: application/json
```

```json
{
  "email": "person@example.test"
}
```

An accepted request returns HTTP 202 with no response body. The endpoint follows
the Access response and must not be used as an account-existence oracle.

## Request phone recovery

```http
POST /bullgate/access/v1/password/recovery/phone
Content-Type: application/json
```

```json
{
  "phone": "+15555550100",
  "applicationClientKey": "android-play"
}
```

Success returns HTTP 200:

```json
{
  "expiresAt": "2026-09-07T12:05:00Z",
  "resendAvailableAt": "2026-09-07T12:01:00Z"
}
```

An empty or whitespace application-client key returns HTTP 400 with
`application-client-invalid` and field `applicationClientKey`.

## Confirm phone recovery

```http
POST /bullgate/access/v1/password/recovery/phone/confirm
Content-Type: application/json
```

```json
{
  "phone": "+15555550100",
  "code": "123456"
}
```

Success returns HTTP 200:

```json
{
  "token": "short-lived-password-reset-token",
  "expiresAt": "2026-09-07T12:10:00Z"
}
```

The returned token is bearer material for the next recovery step. Do not log,
persist longer than necessary, or reuse it for another purpose.

## Reset a recovered password

```http
POST /bullgate/access/v1/password/recovery/reset
Content-Type: application/json
```

```json
{
  "token": "short-lived-password-reset-token",
  "newPassword": "user-supplied-new-password"
}
```

Success returns HTTP 204 with no response body.

## Change the current password

```http
POST /bullgate/access/v1/account/password
Content-Type: application/json
```

```json
{
  "currentPassword": "current-user-password",
  "newPassword": "replacement-user-password"
}
```

The session is selected exclusively from the adapter cookie. Success returns
HTTP 200 with the refreshed session envelope.

## Change the current email

```http
PUT /bullgate/access/v1/account/email
Content-Type: application/json
```

```json
{
  "email": "replacement@example.test"
}
```

The session is selected exclusively from the adapter cookie. Success returns
HTTP 200 with the refreshed session envelope. This SDK contract performs a
direct email change; it does not define a separate email challenge.

## Link Google

```http
POST /bullgate/access/v1/account/google
Content-Type: application/json
```

```json
{
  "idToken": "provider-issued-id-token",
  "accessToken": "provider-issued-access-token"
}
```

Success returns HTTP 200 with the refreshed session envelope.

## Unlink Google

```http
DELETE /bullgate/access/v1/account/google
```

Success returns HTTP 200 with the refreshed session envelope. Access may reject
the operation when unlinking would leave the identity without a usable access
method.

## Link Apple

```http
POST /bullgate/access/v1/account/apple
Content-Type: application/json
```

```json
{
  "identityToken": "provider-issued-identity-token"
}
```

Success returns HTTP 200 with the refreshed session envelope.

## Unlink Apple

```http
DELETE /bullgate/access/v1/account/apple
```

Success returns HTTP 200 with the refreshed session envelope. Access may reject
the operation when unlinking would leave the identity without a usable access
method.

## Start an AccessFlow

```http
POST /bullgate/access/v1/flows
Content-Type: application/json
```

```json
{
  "requestId": "a66bfaba-0ad9-4a84-b6bf-3619c9bf8e04",
  "protocolVersions": [1],
  "intent": "continueRegistration",
  "applicationClientKey": "android-play"
}
```

`requestId` identifies the start intent for idempotent replay.
`protocolVersions` lists the versions accepted by the client. The current SDK
supports version 1. Some intents use the current session cookie, but the client
cannot submit a session token in the JSON body.

Success returns HTTP 200, writes the flow capability cookie, and returns only
the public snapshot:

```json
{
  "flow": {
    "protocolVersion": 1,
    "flowId": "18b52b99-d573-465e-a8ce-21886eb6f958",
    "revision": 1,
    "intent": "continueRegistration",
    "status": "active",
    "expiresAt": "2026-09-07T12:10:00Z",
    "step": {},
    "actions": [],
    "feedback": null,
    "result": null
  },
  "session": null
}
```

The bearer capability is never returned in JSON.

## Read an AccessFlow

```http
GET /bullgate/access/v1/flows/18b52b99-d573-465e-a8ce-21886eb6f958
```

The adapter reads the capability from the flow cookie, refreshes that cookie
from the Access response, and returns HTTP 200 with the public flow envelope.
The path identifier does not replace the capability.

## Act on an AccessFlow

```http
POST /bullgate/access/v1/flows/18b52b99-d573-465e-a8ce-21886eb6f958/actions
Content-Type: application/json
```

```json
{
  "requestId": "5e468ec7-087b-4ffc-b0c4-de36c7a15d50",
  "expectedRevision": 1,
  "action": {
    "id": "d7abb636-7624-44de-8c1c-c5d4a526cb9d",
    "type": "submit",
    "input": {}
  }
}
```

`requestId` provides action-level idempotency. `expectedRevision` prevents a
stale screen or concurrent request from applying an action to the wrong flow
state. `action.id` and `action.type` must come from the current snapshot.

While the flow remains active, success returns HTTP 200 with the next snapshot
and refreshes the flow cookie.

When a terminal action issues a product session, the adapter introspects the
issued token before trusting it, resolves the local product profile, writes the
session cookie, deletes the flow cookie, and returns:

```json
{
  "flow": {
    "protocolVersion": 1,
    "flowId": "18b52b99-d573-465e-a8ce-21886eb6f958",
    "revision": 2,
    "intent": "continueRegistration",
    "status": "completed",
    "expiresAt": "2026-09-07T12:10:00Z",
    "step": null,
    "actions": [],
    "feedback": null,
    "result": {}
  },
  "session": {
    "state": "authenticated",
    "identityId": "4ce297fa-b06d-4af0-8bed-cf6a032d846a",
    "email": "person@example.test",
    "phone": null,
    "phoneVerifiedAt": null,
    "sessionExpiresAt": "2026-09-08T12:00:00Z",
    "hasPassword": true,
    "hasGoogle": false,
    "googleEmail": null,
    "hasApple": false,
    "appleEmail": null,
    "application": {}
  }
}
```

A missing action returns HTTP 400 with `invalid-request` and field `action`.
A completed flow that cannot resolve its required product profile revokes the
issued session and returns HTTP 409 with `application-not-provisioned`.

## Error response

Structured Access and consumer rejections use:

```json
{
  "error": "machine-readable-code",
  "field": "optionalPublicField"
}
```

The BFF preserves the status returned by a structured Access rejection.
Consumer provisioning may return its own explicit status and safe error code
through `BullgateApplicationRejectedException`.

Timeouts, transport failures, invalid JSON, incomplete responses, unsupported
response formats, and unrecognized upstream failures are not treated as
business rejections. When the response has not started,
`UseBullgateAccessFailures()` returns HTTP 503:

```json
{
  "error": "access-unavailable"
}
```

The 503 response uses `Cache-Control: no-store` and does not delete the
session cookie.

The authoritative Access service error catalog is maintained in the
[Access HTTP API documentation](https://github.com/torphi-contise/bullgate-access/blob/main/docs/http-api.md).

## Cookie defaults

| Purpose | Default name | Path | HttpOnly | Secure default | SameSite default |
| --- | --- | --- | --- | --- | --- |
| Session | `bullgate.session` | `/` | yes | always | `Lax` |
| Flow capability | `bullgate.flow` | `/bullgate/access/v1` | yes | always | `Lax` |

Both cookies are host-only because the adapter does not set a Domain attribute.
Writing or deleting either cookie adds no-store response headers. Applications
may change the names, secure policy, and SameSite mode through
`BullgateAccessOptions`; changing these values does not transfer CSRF
responsibility away from the host.
