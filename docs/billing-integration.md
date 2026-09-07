# Billing integration

`Bullgate.Billing.AspNetCore` provides a typed server client, authenticated BFF
routes, and entitlement delivery endpoints. It has no dependency on Bullgate
Access.

## Register the Billing client

```csharp
builder.Services.AddBullgateBillingClient(options =>
{
    options.BaseAddress = new Uri(
        builder.Configuration["Bullgate:Billing:BaseUrl"]!);
    options.IntegrationCredential =
        builder.Configuration["Bullgate:Billing:IntegrationCredential"]!;
});
```

The base URL must use HTTPS and end with `/`. Plain HTTP is an explicit local
development opt-in. The `bgbc_` integration credential belongs only on the
trusted consumer server.

## Map consumer BFF routes

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapBullgateOneTimePurchases(context =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

app.MapBullgateOneTimeOffers(context =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
```

The callback must derive the customer reference from the authenticated server
session. Return `null` to reject the session. Never use customer data supplied
by the request body, query string, or an arbitrary header.

The purchase mapping exposes catalog, preparation, eligibility, and completion.
The offer mapping exposes customer-only list, get, open, and prepare operations.
Administrative offer issue, cancel, list, and inspect operations are available
only through `IBullgateBillingClient`; they are deliberately not mapped into
the customer BFF.

## Purchase sequence

For a new purchase:

1. read the Billing catalog;
2. call preparation;
3. start the native store purchase only when eligible;
4. send the store proof to completion;
5. finalize the store transaction according to the mobile SDK contract.

For recovery, send the existing paid transaction directly to completion. Do not
run preparation or local eligibility first: a negative new-purchase decision
must not discard recovery work.

`Delivered` and `AlreadyDelivered` both mean the benefit is present. A
`billing-store-finalization-pending` exception with
`DeliveryConfirmed=true` means benefit delivery was confirmed but the same
completion request must be retried to finish provider-side work. It never
authorizes another grant.

## Entitlement receiver

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

The consumer implementation must atomically persist the benefit change and a
receipt keyed by environment plus delivery ID. Repeated delivery must not grant
again. A repeated reversal, including a zero business adjustment, must not
reverse again. Retain replay protection after account deletion and serialize a
reversal that arrives before a delayed delivery.

Delivery credentials use the `bgbd_` format and are independent from the
outbound Billing credential. Up to four credentials may be accepted to support
rotation.
