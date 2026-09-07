using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;

namespace Bullgate.Billing.AspNetCore.Tests;

public sealed partial class BillingBffTests
{
    private const string CatalogPath = "/bullgate/billing/v1/catalog/one-time";

    [Fact]
    public async Task Catalog_UsesAuthenticatedIntegration_AndReturnsCommercialTermsIncludingInactiveSkus()
    {
        var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal("/v1/catalog/one-time", request.RequestUri!.AbsolutePath);
            Assert.Equal(Credential, request.Headers.Authorization!.Parameter);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("app-store", body.GetProperty("provider").GetString());
            Assert.Single(body.EnumerateObject());
            return Json(200, new { items = new[] { CatalogItem("sku.active", true), CatalogItem("sku.inactive", false) } });
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(CatalogPath, new { provider = "app-store" });
        var result = await Body(response, HttpStatusCode.OK);
        var items = result.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(80, items[0].GetProperty("entitlementQuantity").GetDecimal());
        Assert.Equal("pack-units", items[0].GetProperty("entitlementKey").GetString());
        Assert.Equal("consumable", items[0].GetProperty("storeProductType").GetString());
        Assert.Equal("repeatable", items[0].GetProperty("purchasePolicy").GetString());
        Assert.False(items[1].GetProperty("isActive").GetBoolean());
        Assert.False(items[0].TryGetProperty("environmentId", out _));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("provider", "google-play")]
    [InlineData("storeProductType", "subscription")]
    [InlineData("purchasePolicy", "unknown")]
    [InlineData("productKey", "")]
    [InlineData("entitlementKey", "")]
    [InlineData("isActive", null)]
    [InlineData("entitlementQuantity", null)]
    public async Task Catalog_RejectsIncompleteOrCrossProviderResponses(string field, string? value)
    {
        var item = JsonSerializer.SerializeToNode(CatalogItem("sku", true))!;
        item[field] = value;
        var handler = new Handler((_, _) => Task.FromResult(Json(200, new { items = new[] { item } })));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(CatalogPath, new { provider = "app-store" });
        Assert.Equal("billing-response-invalid", (await Body(response, HttpStatusCode.ServiceUnavailable)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Catalog_RejectsDuplicateSkus_AndAcceptsEmptyCatalog()
    {
        var duplicate = true;
        var handler = new Handler((_, _) => Task.FromResult(Json(200,
            new { items = duplicate ? new[] { CatalogItem("sku", true), CatalogItem("sku", false) } : Array.Empty<object>() })));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var invalid = await client.PostAsJsonAsync(CatalogPath, new { provider = "app-store" });
        await Body(invalid, HttpStatusCode.ServiceUnavailable);
        duplicate = false;
        using var empty = await client.PostAsJsonAsync(CatalogPath, new { provider = "app-store" });
        Assert.Empty((await Body(empty, HttpStatusCode.OK)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Catalog_RejectsUnauthenticatedRequestsAndCallerOverrides()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("must not call Billing"));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        foreach (var field in new[] { "customerReference", "environmentId", "entitlementQuantity", "isActive" })
        {
            var body = new JsonObject { ["provider"] = "app-store", [field] = "injected" };
            using var response = await client.PostAsJsonAsync(CatalogPath, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var provider = await client.PostAsJsonAsync(CatalogPath, new { provider = "unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, provider.StatusCode);
        client.DefaultRequestHeaders.Add("test-anonymous", "true");
        using var anonymous = await client.PostAsJsonAsync(CatalogPath, new { provider = "app-store" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    private static object CatalogItem(string productId, bool active) => new
    {
        productKey = "one-time-pack", name = "Example pack", provider = "app-store", productId,
        storeProductType = "consumable", purchasePolicy = "repeatable", entitlementKey = "pack-units",
        entitlementQuantity = 80m, isActive = active, environmentId = "must-not-forward",
    };
}
