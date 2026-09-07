# Architecture

The SDK is a server-side integration boundary between an ASP.NET Core consumer
application and Bullgate services.

```text
mobile or web client
        |
        | consumer-authenticated HTTP
        v
consumer ASP.NET Core application
  |     local identity/profile/benefit persistence
  |     Bullgate SDK packages
  |
  +---- integration credential ----> Bullgate Access or Billing
  <---- delivery credential -------- Bullgate Billing entitlement delivery
```

## Package independence

`Bullgate.Access.AspNetCore` and `Bullgate.Billing.AspNetCore` are independent
packages. A consumer may use either package without the other. Billing does not
use Access to authenticate the consumer's end user; the host supplies its own
authentication and derives a trusted customer reference.

## Ownership boundaries

Bullgate Access owns identity, Bullgate sessions, linked login methods, and
AccessFlow state. The consumer owns its product profile, local authorization,
and the mapping from a Bullgate identity to a local subject.

Bullgate Billing owns the commercial catalog, purchase verification, purchase
policy, offer grants, and delivery orchestration. The consumer owns the actual
benefit, its database transaction, and the durable replay guard for each
delivery identifier.

The SDK owns transport adaptation, strict response validation, safe cookie
handling, ASP.NET Core registration, and generic BFF endpoints. It does not own
consumer UI, consumer business policy, databases, store SDK execution, or
deployment topology.

## Deployment model

The SDK has no standalone process, port, database, or deployment. Its assemblies
are build artifacts embedded in the consumer backend. Access and Billing base
URLs always identify separately operated services.
