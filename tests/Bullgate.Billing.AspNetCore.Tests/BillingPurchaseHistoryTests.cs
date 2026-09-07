using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Bullgate.Billing.AspNetCore.Tests;

public sealed class BillingPurchaseHistoryTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static BullgatePurchaseHistoryItem Item() => new(Guid.NewGuid(), Guid.NewGuid(), "google-play",
        "pack.10", "units-10", "Pack", "test", 2, "units", 20, "pending", At, null, null, null, null, null);

    [Theory]
    [InlineData("pending")]
    [InlineData("delivered")]
    [InlineData("policy-blocked")]
    [InlineData("reversal-pending")]
    [InlineData("reversed")]
    public async Task History_is_read_only_generic_and_preserves_delivery_and_reversal_separately(string state)
    {
        var item = Item() with
        {
            DeliveryState = state is "reversal-pending" or "reversed" ? "pending" : state,
            DeliveredAt = state == "delivered" ? At.AddMinutes(1) : null,
            ReversalRequestedAt = state is "reversal-pending" or "reversed" ? At.AddMinutes(2) : null,
            ReversedAt = state == "reversed" ? At.AddMinutes(3) : null,
        };
        using var services = Services(async message =>
        {
            Assert.Equal("/base/v1/purchases/one-time/history", message.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("bgbc_test", message.Headers.Authorization!.Parameter);
            var request = await message.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, request.EnumerateObject().Count());
            Assert.Equal("customer", request.GetProperty("customerReference").GetString());
            Assert.Equal(10, request.GetProperty("offset").GetInt32());
            Assert.Equal(1, request.GetProperty("limit").GetInt32());
            return Reply(new BullgatePurchaseHistoryPage("customer", [item], 11));
        });
        var result = await services.GetRequiredService<IBullgateBillingClient>().ListOneTimePurchaseHistoryAsync(new("customer", 10, 1));
        Assert.Equal(item, Assert.Single(result.Items));
        Assert.Null(item.PaidAmount); // Unknown is not zero; history is not a price lookup.
        Assert.Equal(11, result.NextOffset);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("duplicate")]
    [InlineData("page")]
    [InlineData("purchase-id")]
    [InlineData("quantity")]
    [InlineData("delivery")]
    [InlineData("paid")]
    [InlineData("reversal")]
    [InlineData("blocked-reversal")]
    public async Task Inconsistent_history_is_rejected(string fault)
    {
        var item = Item();
        item = fault switch
        {
            "purchase-id" => item with { PurchaseId = Guid.Empty },
            "quantity" => item with { EntitlementQuantity = 0 },
            "delivery" => item with { DeliveryState = "delivered" },
            "paid" => item with { PaidAmount = 1, PaidCurrency = null },
            "reversal" => item with { ReversedAt = At },
            "blocked-reversal" => item with { DeliveryState = "policy-blocked", ReversalRequestedAt = At },
            _ => item,
        };
        using var services = Services(_ => Task.FromResult(Reply(new BullgatePurchaseHistoryPage(
            fault == "owner" ? "another" : "customer", fault == "duplicate" ? [item, item] : [item], fault == "page" ? 0 : null))));
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() => services.GetRequiredService<IBullgateBillingClient>()
            .ListOneTimePurchaseHistoryAsync(new("customer")));
        Assert.Equal("billing-response-invalid", error.Error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"customerReference\":\"customer\",\"items\":[]}")]
    public async Task Incomplete_success_is_not_empty_history(string json)
    {
        using var services = Services(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }));
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() => services.GetRequiredService<IBullgateBillingClient>()
            .ListOneTimePurchaseHistoryAsync(new("customer")));
        Assert.Equal("billing-response-invalid", error.Error);
    }

    [Theory]
    [InlineData("", 0, 50)]
    [InlineData("customer", -1, 50)]
    [InlineData("customer", 0, 0)]
    [InlineData("customer", 0, 101)]
    public async Task Invalid_request_never_leaves_the_sdk(string customer, int offset, int limit)
    {
        using var services = Services(_ => throw new InvalidOperationException("No HTTP expected"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => services.GetRequiredService<IBullgateBillingClient>()
            .ListOneTimePurchaseHistoryAsync(new(customer, offset, limit)));
    }

    private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static ServiceProvider Services(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var services = new ServiceCollection();
        services.AddBullgateBillingClient(options => { options.BaseAddress = new("https://billing.test/base/"); options.IntegrationCredential = "bgbc_test"; })
            .ConfigurePrimaryHttpMessageHandler(() => new Handler(send));
        return services.BuildServiceProvider();
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
