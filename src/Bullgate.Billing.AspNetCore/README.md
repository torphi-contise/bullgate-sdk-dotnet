# Bullgate.Billing.AspNetCore

Server-side Bullgate Billing integration for ASP.NET Core. The package provides
a typed client, authenticated consumer BFF routes, and a credential-authenticated
entitlement receiver. It does not depend on Bullgate Access.

> Status: version 0.1.0. No public NuGet package has been released yet.

## Register the client

```csharp
builder.Services.AddBullgateBillingClient(options =>
{
    options.BaseAddress = new Uri(
        builder.Configuration["Bullgate:Billing:BaseUrl"]!);
    options.IntegrationCredential =
        builder.Configuration["Bullgate:Billing:IntegrationCredential"]!;
});
```

The URL must use HTTPS, contain no query or fragment, and end with `/`.
`AllowInsecureHttp` exists only for explicitly configured local development.
The `bgbc_` credential must remain on the trusted server.

## Map the consumer BFF

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapBullgateOneTimePurchases(context =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

app.MapBullgateOneTimeOffers(context =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
```

Derive the customer reference only from the authenticated server session.
Returning `null` rejects the session. Never trust a customer reference from a
mobile or browser request.

The purchase BFF maps catalog, prepare, eligibility, and complete operations.
The offer BFF maps customer-only list, get, open, and prepare operations.
Administrative issue, cancel, list, and inspect operations remain server-to-server
methods on `IBullgateBillingClient`.

For recovery, submit an existing paid transaction directly to completion.
Do not run preparation or eligibility first. Preparation is not a reservation
and eligibility is not payment authorization.

## Handle completion failures

`BullgateBillingException` preserves the machine-readable error, retryability,
upstream status, retry delay, public field, resolution requirement, and delivery
confirmation.

When `Error` is `billing-store-finalization-pending` and
`DeliveryConfirmed` is `true`, benefit delivery is confirmed but the same
completion request must be retried to finish provider-side work. Do not start a
new payment or grant the benefit again. `Retryable` alone never confirms
delivery.

## Receive entitlements

```csharp
builder.Services.AddBullgateEntitlementReceiver<MyEntitlementApplication>(
    options =>
    {
        options.EnvironmentKey = "production";
        options.AcceptedCredentials =
        [
            builder.Configuration["Bullgate:Billing:DeliveryCredential"]!,
        ];
    });

app.MapBullgateEntitlementDelivery();
app.MapBullgateEntitlementReversal();
```

Implement `IBullgateEntitlementApplication` so the business mutation and
receipt are committed atomically. Deduplicate by environment plus delivery ID,
reject a reused ID with a different payload, preserve replay protection after
account deletion, and serialize delivery against a reversal that arrives first.

The `bgbd_` delivery credential is independent from the outbound Billing
credential. Up to four credentials may be accepted during rotation.

For full purchase, offer, security, and failure behavior, see the
[repository documentation](https://github.com/torphi-contise/bullgate-sdk-dotnet/blob/main/docs/billing-integration.md).
