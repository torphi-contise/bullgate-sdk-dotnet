using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;

namespace Bullgate.Billing.AspNetCore.Tests;

public sealed partial class BillingBffTests
{
    private const string OffersPrefix = "/bullgate/billing/v1/offers/one-time/grants/";
    private static readonly string[] OfferReadOperations = ["get", "open"];
    private static readonly Guid GrantId = Guid.NewGuid();

    [Fact]
    public async Task Offers_UseOnlySessionCustomerAndGrant_AndReturnExactServerCheckout()
    {
        var operations = new List<string>();
        var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal(Credential, request.Headers.Authorization!.Parameter);
            Assert.False(request.Headers.Contains("Cookie"));
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(Customer, body.GetProperty("customerReference").GetString());
            Assert.False(body.TryGetProperty("productId", out _));
            Assert.False(body.TryGetProperty("storeOfferId", out _));
            var operation = request.RequestUri!.Segments.Last();
            operations.Add(operation);
            if (operation == "available") return Json(200, new { items = new[] { OfferBody(GrantId) }, nextOffset = (int?)null });
            Assert.Equal(GrantId, body.GetProperty("grantId").GetGuid());
            if (operation == "prepare")
            {
                Assert.Equal("google-play", body.GetProperty("provider").GetString());
                Assert.Equal(3, body.EnumerateObject().Count());
                return Json(200, PreparationBody());
            }
            Assert.Equal(2, body.EnumerateObject().Count());
            return Json(200, OfferBody(GrantId, operation == "open" ? "opened" : "sent"));
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "private-mobile-cookie");
        using var listed = await client.PostAsJsonAsync(OffersPrefix + "available", new { provider = "google-play" });
        var offers = (await Body(listed, HttpStatusCode.OK)).GetProperty("items");
        Assert.Single(offers.EnumerateArray());
        Assert.Equal("popup", offers[0].GetProperty("placements")[0].GetString());
        Assert.False(offers[0].TryGetProperty("showsPopup", out _));
        foreach (var operation in OfferReadOperations)
        {
            using var response = await client.PostAsJsonAsync(OffersPrefix + operation, new { grantId = GrantId });
            Assert.Equal(operation == "open" ? "opened" : "sent", (await Body(response, HttpStatusCode.OK)).GetProperty("status").GetString());
        }
        using var prepared = await client.PostAsJsonAsync(OffersPrefix + "prepare", new { grantId = GrantId, provider = "google-play" });
        var preparation = await Body(prepared, HttpStatusCode.OK);
        Assert.Equal(GrantId, preparation.GetProperty("grantId").GetGuid());
        Assert.Equal("server-selected-sku", preparation.GetProperty("productId").GetString());
        Assert.Equal("welcome-offer", preparation.GetProperty("storeOfferId").GetString());
        Assert.Equal("buy", preparation.GetProperty("purchaseOptionId").GetString());
        Assert.Equal("consumable", preparation.GetProperty("storeProductType").GetString());
        Assert.Equal("once-per-customer", preparation.GetProperty("purchasePolicy").GetString());
        Assert.Equal(StoreAccountToken, preparation.GetProperty("storeAccountToken").GetGuid());
        Assert.False(preparation.TryGetProperty("offerToken", out _));
        Assert.Equal(new[] { "available", "get", "open", "prepare" }, operations);
    }

    [Fact]
    public async Task Offers_RejectMobileManagementAndOverrides_BeforeCallingBilling()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("must not call Billing"));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        foreach (var field in new[] { "customerReference", "environmentId", "productId", "storeOfferId", "purchaseOptionId", "storeAccountToken" })
        {
            var body = new JsonObject { ["grantId"] = GrantId.ToString("D"), ["provider"] = "google-play", [field] = "injected" };
            using var response = await client.PostAsJsonAsync(OffersPrefix + "prepare", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var operation in new[] { "issue", "cancel", "inspect" })
        {
            using var response = await client.PostAsJsonAsync(OffersPrefix + operation, new { grantId = GrantId });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        client.DefaultRequestHeaders.Add("test-anonymous", "true");
        foreach (var operation in new[] { "available", "get", "open", "prepare" })
        {
            using var response = await client.PostAsJsonAsync(OffersPrefix + operation, new { grantId = GrantId });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("grantId")]
    [InlineData("provider")]
    [InlineData("purchaseOptionId")]
    [InlineData("storeProductType")]
    [InlineData("expiresAt")]
    [InlineData("storeAccountToken")]
    public async Task Offers_RejectInvalidOrCrossGrantPreparation_WithoutOrdinaryCheckoutFallback(string field)
    {
        var body = JsonSerializer.SerializeToNode(PreparationBody())!;
        body[field] = field switch
        {
            "grantId" => Guid.NewGuid().ToString("D"),
            "provider" => "app-store",
            "expiresAt" => DateTimeOffset.UtcNow.AddDays(-1).ToString("O"),
            "storeProductType" => "subscription",
            "storeAccountToken" => Guid.Empty.ToString("D"),
            _ => null,
        };
        var handler = new Handler((request, _) =>
        {
            Assert.EndsWith("offers/one-time/grants/prepare", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(200, body));
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(OffersPrefix + "prepare", new { grantId = GrantId, provider = "google-play" });
        Assert.Equal("billing-response-invalid", (await Body(response, HttpStatusCode.ServiceUnavailable)).GetProperty("error").GetString());
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(403, "billing-integration-unauthorized", 503, "google-play")]
    [InlineData(404, "billing-offer-grant-not-found", 404, "google-play")]
    [InlineData(409, "billing-offer-provider-mismatch", 409, "app-store")]
    [InlineData(503, "billing-purchase-preparation-unavailable", 503, "google-play")]
    public async Task OfferErrors_PreserveRetryAndDoNotReportDeliveryOrLogUserOut(int upstream, string error, int expected, string provider)
    {
        var handler = new Handler((_, _) =>
        {
            var response = Json(upstream, new { error, retryable = upstream == 503 });
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return Task.FromResult(response);
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(OffersPrefix + "prepare", new { grantId = GrantId, provider });
        var failure = await Body(response, (HttpStatusCode)expected);
        Assert.Equal(error, failure.GetProperty("error").GetString());
        Assert.False(failure.GetProperty("deliveryConfirmed").GetBoolean());
        Assert.Equal(upstream == 503, failure.GetProperty("retryable").GetBoolean());
        if (upstream != 403) Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter!.Delta);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OfferPages_AcceptBoundedMetadataBeyondOldSinglePurchaseBuffer()
    {
        var items = Enumerable.Range(0, 50).Select(_ => OfferBody(Guid.NewGuid(), name: new string('n', 160))).ToArray();
        var handler = new Handler((_, _) => Task.FromResult(Json(200, new { items, nextOffset = 50 })));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(OffersPrefix + "available", new { limit = 50 });
        var result = await Body(response, HttpStatusCode.OK);
        Assert.Equal(50, result.GetProperty("items").GetArrayLength());
        Assert.Equal(50, result.GetProperty("nextOffset").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("url")]
    [InlineData("token")]
    [InlineData("option")]
    public async Task AppleOfferPreparationUsesAuthenticatedCustomerAndUrl_WithoutPurchaseHistory(string? invalid)
    {
        var handler = new Handler(async (request, ct) =>
        {
            var sent = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(Customer, sent.GetProperty("customerReference").GetString());
            Assert.Equal("app-store", sent.GetProperty("provider").GetString());
            Assert.Equal(GrantId, sent.GetProperty("grantId").GetGuid());
            Assert.Equal(3, sent.EnumerateObject().Count());
            return Json(200, new {
                grantId = GrantId, provider = "app-store", productId = "server-selected-sku", storeProductType = "consumable",
                storeOfferId = "welcome-offer", purchasePolicy = "repeatable", expiresAt = DateTimeOffset.UtcNow.AddDays(1),
                purchaseOptionId = invalid == "option" ? "buy" : null,
                storeAccountToken = invalid == "token" ? StoreAccountToken : (Guid?)null,
                redemptionUrl = invalid == "url" ? "https://evil.example/redeem" : "https://apps.apple.com/redeem?id=123&code=WELCOME",
            });
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(OffersPrefix + "prepare", new { grantId = GrantId, provider = "app-store" });
        var body = await Body(response, invalid is null ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        if (invalid is not null) Assert.Equal("billing-response-invalid", body.GetProperty("error").GetString());
        else
        {
            Assert.Equal("https://apps.apple.com/redeem?id=123&code=WELCOME", body.GetProperty("redemptionUrl").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("storeAccountToken").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("purchaseOptionId").ValueKind);
        }
        Assert.Equal(1, handler.Calls);
    }

    private static object PreparationBody() => new
    {
        grantId = GrantId, provider = "google-play", productId = "server-selected-sku", storeProductType = "consumable",
        storeOfferId = "welcome-offer", purchaseOptionId = "buy", purchasePolicy = "once-per-customer",
        storeAccountToken = StoreAccountToken, expiresAt = DateTimeOffset.UtcNow.AddDays(1),
    };
    private static object OfferBody(Guid grantId, string status = "sent", string name = "Welcome") => new
    {
        grantId, offerKey = "welcome", name, category = "acquisition", provider = "google-play", productId = "server-selected-sku",
        storeOfferId = "welcome-offer", purchaseOptionId = "buy", redemptionUrl = (string?)null,
        placements = new[] { "popup" }, status, createdAt = DateTimeOffset.UtcNow, expiresAt = DateTimeOffset.UtcNow.AddDays(1),
        customerReference = "must-not-forward-upstream-customer", actorReference = "private-operator",
    };
}
