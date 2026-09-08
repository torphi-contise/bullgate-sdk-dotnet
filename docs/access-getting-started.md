# Access getting started

This guide connects an ASP.NET Core consumer application to Bullgate Access
through `Bullgate.Access.AspNetCore`. It establishes the minimum secure boundary
and then routes deeper questions to the canonical integration and HTTP guides.

The SDK runs inside the consumer backend. Browser and mobile clients call the
consumer BFF; they never receive the Access integration credential, session
token, reset token, provider credential, or AccessFlow capability.

## 1. Reference the package

No public NuGet package is recorded in the current project status. When working
from a source checkout, reference the Access project directly:

```xml
<ItemGroup>
  <ProjectReference Include="..\bullgate-sdk-dotnet\src\Bullgate.Access.AspNetCore\Bullgate.Access.AspNetCore.csproj" />
</ItemGroup>
```

Replace the relative path with the location of the repository in your own
workspace. Do not treat a local project version or CI artifact as a published
package. Recheck `docs/project-status.md` before documenting distribution.

## 2. Configure the server boundary

Keep the Bullgate Access base URL in server configuration and the `bgic_`
integration credential in a server-side secret manager. Never place the
credential in source, browser JavaScript, a mobile binary, or public
configuration.

```csharp
builder.Services.AddBullgateAccess(options =>
{
    options.BaseAddress = new Uri(
        builder.Configuration["Bullgate:Access:BaseUrl"]!);
    options.IntegrationCredential =
        builder.Configuration["Bullgate:Access:IntegrationCredential"]!;
});
```

Startup validation requires:

- an absolute `BaseAddress`;
- an integration credential beginning with `bgic_`;
- a positive request timeout;
- non-empty, distinct session and flow cookie names.

The default request timeout is ten seconds. The default cookies are
`bullgate.session` and `bullgate.flow`. Change those defaults only with an
explicit deployment reason and a session-impact plan.

## 3. Define the consumer-owned types

Access owns identity and session facts. The consumer owns registration data and
the product profile returned to its clients.

```csharp
public sealed record Registration(string DisplayName);

public sealed record ProductProfile(
    string Subject,
    string DisplayName);
```

Do not duplicate email, phone, verification, or linked-provider state in
`ProductProfile` merely to satisfy the SDK. Those facts remain in the Bullgate
session envelope.

## 4. Implement the product-profile boundary

Implement `IBullgateAccessApplication<TRegistration,TApplication>` using the
consumer's durable persistence. Provisioning must be idempotent: a retry for the
same Bullgate identity must return the already committed profile instead of
creating a duplicate.

```csharp
public sealed class ProductAccessApplication(ProductProfiles profiles)
    : IBullgateAccessApplication<Registration, ProductProfile>
{
    public ValueTask<BullgateApplicationProvisionResult<ProductProfile>>
        ProvisionAsync(
            BullgateSession session,
            Registration registration,
            CancellationToken cancellationToken) =>
        profiles.ProvisionAsync(
            session.IdentityId,
            registration.DisplayName,
            cancellationToken);

    public ValueTask<ProductProfile?> ResolveAsync(
        BullgateSession session,
        CancellationToken cancellationToken) =>
        profiles.FindByBullgateIdentityIdAsync(
            session.IdentityId,
            cancellationToken);
}
```

`ProductProfiles` represents consumer-owned persistence in this example. Its
provision operation must atomically enforce one product profile for one Bullgate
identity and return:

```csharp
new BullgateApplicationProvisionResult<ProductProfile>(profile)
```

The SDK does not create that transaction or database constraint for the
consumer.

## 5. Resolve the local ASP.NET Core principal

Implement `IBullgatePrincipalResolver` from trusted consumer state. The
resolver runs only after online introspection returns an active product-purpose
session.

```csharp
public sealed class PrincipalResolver(ProductProfiles profiles)
    : IBullgatePrincipalResolver
{
    public async ValueTask<BullgateLocalPrincipal?> ResolveAsync(
        BullgateSession session,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByBullgateIdentityIdAsync(
            session.IdentityId,
            cancellationToken);

        return profile is null
            ? null
            : new BullgateLocalPrincipal(
                profile.Subject,
                profile.DisplayName);
    }
}
```

Return `null` when no trusted local subject exists. Never accept the local
subject or authorization claims from browser or mobile input.

## 6. Register authentication and consumer adapters

Use the same `Registration` and `ProductProfile` types when registering the
consumer adapter and mapping endpoints.

```csharp
builder.Services
    .AddAuthentication(BullgateAccessDefaults.AuthenticationScheme)
    .AddBullgateSession();

builder.Services.AddAuthorization();
builder.Services.AddScoped<IBullgatePrincipalResolver, PrincipalResolver>();
builder.Services.AddScoped<
    IBullgateAccessApplication<Registration, ProductProfile>,
    ProductAccessApplication>();
```

Register `ProductProfiles` with the lifetime required by the consumer's actual
persistence implementation.

## 7. Configure the request pipeline

```csharp
app.UseHttpsRedirection();
app.UseBullgateAccessFailures();
app.UseAuthentication();
app.UseAuthorization();

app.MapBullgateAccess<Registration, ProductProfile>();
```

Place `UseBullgateAccessFailures` before authentication and mapped endpoints so
it can translate `BullgateAccessUnavailableException` before a response starts.
It returns no-store HTTP 503 with `access-unavailable` and intentionally
preserves a potentially valid session cookie.

The mapped BFF route prefix is `/bullgate/access/v1`.

## 8. Configure browser requests

The browser must accept and return the adapter cookies. A cross-origin request
normally needs credential mode enabled:

```javascript
const response = await fetch("/bullgate/access/v1/session", {
  credentials: "include",
});
```

The consumer still owns its HTTPS, trusted-origin, CORS, reverse-proxy, and CSRF
configuration. `HttpOnly`, `Secure`, and `SameSite=Lax` defaults are not a
complete CSRF policy.

The default secure-cookie policy means plain HTTP development requests do not
receive the session cookie back. Prefer local HTTPS. Any development-only
override must be explicit and must not weaken deployed environments.

## 9. Understand the first session states

`GET /bullgate/access/v1/session` returns a public session envelope when an
active Bullgate session is available.

- `state: "authenticated"` means Access returned a product-purpose session and
  the consumer resolved the required product profile.
- `state: "registration"` means the identity has a valid registration-purpose
  session. It can continue registration but does not authenticate protected
  product routes.
- HTTP 401 means no usable authenticated product principal was established. It
  does not prove that an account is absent.
- HTTP 403 means authentication succeeded but consumer authorization denied the
  operation.
- HTTP 503 with `access-unavailable` means the adapter could not obtain a
  trustworthy Access result. It does not prove that the session is invalid.

Session tokens and flow capabilities never appear in the envelope.

## 10. Continue with the task-specific guide

- For ownership, account deletion, direct-client use, and session behavior, see
  [Access integration](access-integration.md).
- For every BFF route, body, status, error, and cookie effect, see
  [Access BFF HTTP API](access-http-api.md).
- For browser, session, flow, and startup failures, see
  [Access troubleshooting](access-troubleshooting.md).
- For secret, cookie, and CSRF boundaries, see
  [Security model](security-model.md).
- For exception and availability semantics, see
  [Failure model](failure-model.md).

## First-integration checklist

- [ ] The integration credential exists only in trusted server configuration.
- [ ] `BaseAddress` targets the intended Access environment.
- [ ] Product provisioning is idempotent and durably keyed by Bullgate identity.
- [ ] The principal resolver uses trusted consumer state.
- [ ] Middleware order matches this guide.
- [ ] Browser requests use the intended origin and cookie credential mode.
- [ ] HTTPS, CORS, CSRF, and reverse-proxy behavior are configured by the host.
- [ ] Registration-purpose sessions are not accepted as product authentication.
- [ ] Logs and diagnostics contain no credentials, cookies, tokens, OTPs, or
      provider assertions.
- [ ] The exact SDK and Access service revisions intended for deployment have
      been validated together.
