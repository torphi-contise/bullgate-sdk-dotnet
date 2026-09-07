# Bullgate SDK for .NET

Official .NET integration packages for Bullgate. The SDK connects ASP.NET Core
applications to Bullgate services without moving product data, authorization
rules, or business persistence into Bullgate.

The repository currently contains two independent packages:

| Package | Purpose |
| --- | --- |
| `Bullgate.Access.AspNetCore` | Typed Access client, host-only session cookies, ASP.NET Core authentication, and an authenticated BFF. |
| `Bullgate.Billing.AspNetCore` | Typed Billing client, authenticated purchase and offer BFFs, and an idempotent entitlement receiver contract. |

Both packages target .NET 10. They are build-time dependencies, not separately
deployed services. Versioned package distribution has not been published yet;
do not assume that a public NuGet feed already exists.

## Start here

- [Documentation map](docs/README.md)
- [Access integration](docs/access-integration.md)
- [Access BFF HTTP API](docs/access-http-api.md)
- [Access troubleshooting](docs/access-troubleshooting.md)
- [Billing integration](docs/billing-integration.md)
- [Security model](docs/security-model.md)
- [Failure model](docs/failure-model.md)
- [Project status](docs/project-status.md)
- [Compatibility](docs/compatibility.md)
- [Testing](docs/testing.md)
- [Changelog](CHANGELOG.md)

Package-specific quick starts are also embedded in the NuGet packages:

- [Bullgate.Access.AspNetCore](src/Bullgate.Access.AspNetCore/README.md)
- [Bullgate.Billing.AspNetCore](src/Bullgate.Billing.AspNetCore/README.md)

## Minimal Access setup

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

app.UseBullgateAccessFailures();
app.UseAuthentication();
app.UseAuthorization();
app.MapBullgateAccess<MyRegistration, MyApplication>();
```

The host implements `IBullgatePrincipalResolver` and
`IBullgateAccessApplication<TRegistration, TApplication>` to map a Bullgate
identity to its local subject and product profile.

## Minimal Billing setup

```csharp
builder.Services.AddBullgateBillingClient(options =>
{
    options.BaseAddress = new Uri(
        builder.Configuration["Bullgate:Billing:BaseUrl"]!);
    options.IntegrationCredential =
        builder.Configuration["Bullgate:Billing:IntegrationCredential"]!;
});

app.UseAuthentication();
app.UseAuthorization();
app.MapBullgateOneTimePurchases(context =>
    context.User.FindFirst(
        System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
```

The customer reference must come from the authenticated server session. Never
accept it from an app request body, query string, or arbitrary header.

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet restore Bullgate.Sdk.DotNet.slnx
dotnet build Bullgate.Sdk.DotNet.slnx --configuration Release --no-restore
dotnet test Bullgate.Sdk.DotNet.slnx --configuration Release --no-build
dotnet pack Bullgate.Sdk.DotNet.slnx --configuration Release --no-build
```

## Security boundary

Integration and delivery credentials belong only on trusted servers. Session
tokens and flow capabilities remain in `HttpOnly` cookies managed by the Access
adapter. The Billing BFF derives the customer from the authenticated host
session, while the consumer application remains responsible for local
authorization, CSRF protection, benefit persistence, and atomic replay guards.

See [SECURITY.md](SECURITY.md) for private vulnerability reporting.

## Contributing and license

Contributions are welcome under the [contribution guide](CONTRIBUTING.md) and
the [Developer Certificate of Origin 1.1](DCO). This project is licensed under
the [Apache License 2.0](LICENSE).

Package maintainers should follow the documented
[release process](docs/release-process.md). Registry selection and package
publication remain pending.
