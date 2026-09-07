using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Bullgate.Billing.AspNetCore.Tests;

public sealed class BillingOfferAdministrationTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid GrantId = Guid.NewGuid();
    private static readonly BullgateOfferCatalogItem Google = new("welcome", "Welcome", "acquisition", "google-play",
        "pack.10", "discount", "buy", null, "units-10", "units", 10, true);

    [Theory]
    [InlineData("sent", false)]
    [InlineData("expired", true)]
    [InlineData("canceled", true)]
    public async Task Issue_sends_original_expiration_actor_and_optional_note_with_no_caller_scope(string status, bool replay)
    {
        var expires = Created.AddDays(7);
        using var services = Services(async message =>
        {
            Assert.Equal("/base/v1/offers/one-time/grants/issue", message.RequestUri!.AbsolutePath);
            Assert.Equal("bgbc_test", message.Headers.Authorization!.Parameter);
            var request = await message.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(7, request.EnumerateObject().Count());
            Assert.Equal("customer", request.GetProperty("customerReference").GetString());
            Assert.Equal("operation", request.GetProperty("idempotencyKey").GetString());
            Assert.Equal("operator", request.GetProperty("actorReference").GetString());
            Assert.Equal(expires, request.GetProperty("expiresAt").GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, request.GetProperty("reason").ValueKind);
            Assert.Equal("dashboard", request.GetProperty("placements")[0].GetString());
            return Reply(new BullgateIssuedOffer(GrantId, status, Created, expires, replay));
        });
        var result = await services.GetRequiredService<IBullgateBillingClient>().IssueOneTimeOfferAsync(
            new("customer", "welcome", "operation", "operator", null, expires, ["dashboard"]));
        Assert.Equal(status, result.Status);
        Assert.Equal(replay, result.AlreadyExists);
    }

    [Fact]
    public async Task Catalog_includes_store_binding_and_generic_entitlement_for_a_customer_without_purchases()
    {
        using var services = Services(async message =>
        {
            Assert.EndsWith("/catalog", message.RequestUri!.AbsolutePath);
            var request = await message.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("new-customer", request.GetProperty("customerReference").GetString());
            Assert.Equal(3, request.EnumerateObject().Count());
            return Reply(new BullgateOfferCatalog([Google, Google with { OfferKey = "apple-welcome", Provider = "app-store",
                PurchaseOptionId = null, RedemptionUrl = "https://apps.apple.com/redeem?id=123&code=WELCOME" }], null));
        });
        var result = await services.GetRequiredService<IBullgateBillingClient>().ListSendableOneTimeOffersAsync(new("new-customer"));
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("units", result.Items[0].EntitlementKey);
        Assert.Equal(10, result.Items[0].EntitlementQuantity);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task History_preserves_inactive_expired_grants_and_pagination_without_marking_opened()
    {
        using var services = Services(async message =>
        {
            Assert.EndsWith("/list", message.RequestUri!.AbsolutePath);
            var request = await message.Content!.ReadFromJsonAsync<BullgateOfferManagementListRequest>();
            Assert.Equal(new("customer", 10, 10), request);
            return Reply(new BullgateAdministrativeOfferGrants([
                new(GrantId, Google with { IsActive = false }, "expired", [], Created, Created.AddHours(1), null, null, null)], 11, 10, 10));
        });
        var history = await services.GetRequiredService<IBullgateBillingClient>().ListOneTimeOfferHistoryAsync(new("customer", 10, 10));
        Assert.False(Assert.Single(history.Items).Offer.IsActive);
        Assert.Equal("expired", history.Items[0].Status);
        Assert.Null(history.Items[0].OpenedAt);
        Assert.Equal(11, history.Total);
    }

    [Fact]
    public async Task Cancel_and_inspect_keep_ownership_and_optional_audit_note()
    {
        using var services = Services(async message =>
        {
            var request = await message.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("customer", request.GetProperty("customerReference").GetString());
            Assert.Equal(GrantId, request.GetProperty("grantId").GetGuid());
            if (message.RequestUri!.AbsolutePath.EndsWith("/cancel"))
            {
                Assert.Equal("operator", request.GetProperty("actorReference").GetString());
                Assert.Equal(JsonValueKind.Null, request.GetProperty("reason").ValueKind);
                return Reply(new BullgateOfferStatus(GrantId, "canceled"));
            }
            Assert.EndsWith("/inspect", message.RequestUri.AbsolutePath);
            return Reply(new BullgateOfferGrantHistory(GrantId, "customer", "welcome", "canceled", Created, Created.AddDays(7),
                null, Created.AddHours(1), [new("canceled", "backoffice", "operator", null, Created.AddHours(1))], null));
        });
        var client = services.GetRequiredService<IBullgateBillingClient>();
        Assert.Equal("canceled", (await client.CancelOneTimeOfferAsync(new("customer", GrantId, "operator"))).Status);
        Assert.Null(Assert.Single((await client.InspectOneTimeOfferAsync(new("customer", GrantId))).Audit).Reason);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("list")]
    [InlineData("issue")]
    [InlineData("cancel")]
    [InlineData("inspect")]
    public async Task Incomplete_success_payload_is_rejected(string operation)
    {
        using var services = Services(_ => Task.FromResult(Reply(new { grantId = GrantId, status = "sent" })));
        var client = services.GetRequiredService<IBullgateBillingClient>();
        var error = await Assert.ThrowsAsync<BullgateBillingException>(async () =>
        {
            switch (operation)
            {
                case "catalog": await client.ListSendableOneTimeOffersAsync(new("customer")); break;
                case "list": await client.ListOneTimeOfferHistoryAsync(new("customer")); break;
                case "issue": await client.IssueOneTimeOfferAsync(new("customer", "welcome", "op", "actor", null, Created.AddDays(7), [])); break;
                case "cancel": await client.CancelOneTimeOfferAsync(new("customer", GrantId, "actor")); break;
                default: await client.InspectOneTimeOfferAsync(new("customer", GrantId)); break;
            }
        });
        Assert.Equal("billing-response-invalid", error.Error);
    }

    [Theory]
    [InlineData(401, "billing-integration-unauthorized", false)]
    [InlineData(409, "billing-offer-not-eligible", false)]
    [InlineData(503, "billing-unavailable", true)]
    public async Task Administrative_operations_preserve_transport_failure_contract(int status, string code, bool retryable)
    {
        using var services = Services(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = JsonContent.Create(new { error = code, retryable }) }));
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() =>
            services.GetRequiredService<IBullgateBillingClient>().ListSendableOneTimeOffersAsync(new("customer")));
        Assert.Equal(code, error.Error);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(retryable, error.Retryable);
    }

    private static ServiceProvider Services(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle)
    {
        var services = new ServiceCollection();
        services.AddBullgateBillingClient(options => {
            options.BaseAddress = new("https://billing.test/base/"); options.IntegrationCredential = "bgbc_test";
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler(handle));
        return services.BuildServiceProvider();
    }
    private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
}
