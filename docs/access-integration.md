# Access integration

`Bullgate.Access.AspNetCore` provides a typed client, host-only cookies,
authentication, and a BFF for Bullgate Access.

## Register services

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

`BaseAddress` must be absolute. The integration credential must use the
`bgic_` format. Store it in the server's secret manager, never in source,
browser JavaScript, a mobile binary, or public configuration.

## Configure the pipeline

```csharp
app.UseBullgateAccessFailures();
app.UseAuthentication();
app.UseAuthorization();

app.MapBullgateAccess<Registration, ProductProfile>();
```

Place the failure middleware before components that can throw
`BullgateAccessUnavailableException`. The mapped BFF uses
`/bullgate/access/v1` and keeps session tokens and flow capabilities in
separate `HttpOnly` cookies.

## Implement the consumer boundary

`IBullgatePrincipalResolver` maps a product-purpose Bullgate session to the
local subject used by ASP.NET Core authentication. Returning `null` rejects
local authentication.

`IBullgateAccessApplication<TRegistration,TApplication>` provisions and
resolves the consumer's product profile. Provisioning must be idempotent because
a request may be retried. Identity fields such as email, phone, verification
state, and linked providers remain in the Bullgate session envelope and should
not be redefined as product fields.

## Session behavior

Every request carrying the session cookie is introspected against Access. A
missing or inactive session is unauthenticated and removes the cookie. A valid
registration-purpose session remains available through
`IBullgateSessionFeature` but does not create an authenticated product
principal. Only a product-purpose session can do so.

An unavailable Access service returns HTTP 503 with
`{"error":"access-unavailable"}`. The adapter preserves the cookie because
unavailability does not prove that the session is invalid.

## Account deletion

The SDK exposes `IBullgateAccessClient.DeleteAccountAsync`, but it does not
coordinate deletion across consumer and Access databases. The consumer owns the
order, retry policy, local data deletion, and user-visible result.

## Direct client

Use `IBullgateAccessClient` only from trusted server code. It covers
registration and login, social sign-in and linking, session operations,
password recovery, account maintenance, application-client configuration, and
AccessFlow commands. See the generated XML documentation for individual
contracts and parameters.
