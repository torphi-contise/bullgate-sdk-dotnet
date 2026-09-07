# Bullgate.Access.AspNetCore

ASP.NET Core adapter for Bullgate Access. The package provides a typed
server-to-server client, host-only session and flow cookies, an authentication
handler, failure middleware, and an authenticated BFF.

> Status: version 0.1.0. No public NuGet package has been released yet.

## Setup

```csharp
builder.Services.AddBullgateAccess(options =>
{
    options.BaseAddress = new Uri(
        builder.Configuration["Bullgate:Access:BaseUrl"]!);
    options.IntegrationCredential =
        builder.Configuration["Bullgate:Access:IntegrationCredential"]!;
});

builder.Services
    .AddAuthentication(BullgateAccessDefaults.AuthenticationScheme)
    .AddBullgateSession();

builder.Services.AddScoped<IBullgatePrincipalResolver, PrincipalResolver>();
builder.Services.AddScoped<
    IBullgateAccessApplication<Registration, ProductProfile>,
    ProductAccessApplication>();
```

Then configure the middleware and endpoints:

```csharp
app.UseBullgateAccessFailures();
app.UseAuthentication();
app.UseAuthorization();
app.MapBullgateAccess<Registration, ProductProfile>();
```

The integration credential is a server secret in the `bgic_` format.
`BaseAddress` and the credential are validated when the application starts.

## Consumer adapters

`IBullgatePrincipalResolver` maps an introspected product-purpose Bullgate
session to the local subject used by ASP.NET Core. Return `null` when no trusted
local principal exists.

`IBullgateAccessApplication<TRegistration,TApplication>` provisions or
resolves the consumer-owned product profile. Provisioning must be idempotent.
Email, phone, verification state, linked providers, and Bullgate session state
remain identity data and are already present in the session envelope.

## Cookies and authentication

The default session cookie is `bullgate.session` with `Path=/`. The default
flow cookie is `bullgate.flow` and is scoped to
`/bullgate/access/v1`. Both are host-only and `HttpOnly`; secure cookies and
`SameSite=Lax` are the defaults.

The handler introspects each presented session. A registration-purpose session
is available from `IBullgateSessionFeature` but does not authenticate a product
principal. An unavailable Access service produces HTTP 503 through
`UseBullgateAccessFailures` and does not delete the cookie.

## BFF surface

`MapBullgateAccess<TRegistration,TApplication>()` maps registration, login,
Google and Apple authentication, session, logout, password recovery, account
maintenance, public application-client configuration, and AccessFlow routes
under `/bullgate/access/v1`.

Session tokens and flow capabilities never appear in the JSON response. The
consumer application owns CSRF protection, CORS, local authorization, profile
persistence, and any cross-database account-deletion workflow.

For detailed setup and ownership rules, see the
[Access integration guide](https://github.com/torphi-contise/bullgate-sdk-dotnet/blob/main/docs/access-integration.md).
For exact BFF requests and responses, see the
[Access BFF HTTP API](https://github.com/torphi-contise/bullgate-sdk-dotnet/blob/main/docs/access-http-api.md).
User-visible changes are recorded in the
[repository changelog](https://github.com/torphi-contise/bullgate-sdk-dotnet/blob/main/CHANGELOG.md).
Framework, platform, protocol, and support claims are defined in the
[compatibility policy](https://github.com/torphi-contise/bullgate-sdk-dotnet/blob/main/docs/compatibility.md).
Common integration failures are covered by the
[Access troubleshooting guide](https://github.com/torphi-contise/bullgate-sdk-dotnet/blob/main/docs/access-troubleshooting.md).
