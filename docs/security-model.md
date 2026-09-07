# Security model

## Secrets

- Access `bgic_` and Billing `bgbc_` integration credentials are outbound
  server secrets.
- Billing `bgbd_` delivery credentials authenticate inbound entitlement calls.
- Bullgate session tokens and AccessFlow capabilities are bearer secrets held in
  host-only `HttpOnly` cookies.
- Never commit, log, return, or send these values to browser JavaScript or a
  mobile application.

Use an external secret manager and rotate credentials after suspected exposure.

## End-user identity

The Billing SDK does not trust a customer reference received from a client. The
consumer host authenticates the end user and resolves the reference through the
callback passed to each BFF mapping.

The Access authentication handler trusts a product-purpose session only after
online introspection and successful resolution by
`IBullgatePrincipalResolver`. Additional claims returned by the resolver are
consumer-owned and must be derived from trusted local state.

## Cookies and CSRF

The Access adapter writes host-only, `HttpOnly` cookies. Secure cookies are the
default. `SameSite=Lax` is the default but is not a complete CSRF defense for
every application. The host remains responsible for its origin policy, CSRF
controls, CORS configuration, proxy correctness, and HTTPS termination.

## Entitlement integrity

Authentication alone does not make delivery idempotent. The consumer must commit
the business mutation and delivery receipt in one database transaction, reject
a reused delivery ID with a different payload, and retain enough history to
prevent a delayed replay after deletion or reversal.

## Response validation

The SDK validates successful Bullgate responses before returning them. Invalid
success payloads are treated as upstream failures rather than partially trusted
data. HTTP redirects and automatic cookie handling are disabled for the Billing
client.
