using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bullgate.Billing.AspNetCore.Tests;

public sealed partial class BillingBffTests
{
    private const string Prefix = "/bullgate/billing/v1/purchases/one-time/";
    private const string Credential = "bgbc_private-server-credential";
    private const string Customer = "server-session-customer";
    private static readonly string[] CompletionStatuses = ["delivered", "already-delivered"];
    private static readonly Guid StoreAccountToken = Guid.Parse("2c6a6a6e-cfe6-40e8-8a47-c2c8023db63e");

    [Theory]
    [InlineData("app-store")]
    [InlineData("google-play")]
    public async Task Bff_UsesServerCustomer_AndReturnsTheMobileContract(string provider)
    {
        var operations = new List<string>();
        var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal(Credential, request.Headers.Authorization!.Parameter);
            Assert.False(request.Headers.Contains("Cookie"));
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(Customer, body.GetProperty("customerReference").GetString());
            Assert.Equal(provider, body.GetProperty("provider").GetString());
            Assert.Equal("pack.10", body.GetProperty("productId").GetString());
            var operation = request.RequestUri!.Segments.Last();
            operations.Add(operation);
            Assert.Equal(operation == "complete" ? 4 : 3, body.EnumerateObject().Count());
            if (operation == "complete")
            {
                Assert.Equal("private-proof", body.GetProperty("proof").GetString());
                return Json(200, new { status = operations.Count == 3 ? "delivered" : "already-delivered" });
            }
            return Json(200, new { purchasePolicy = "repeatable", eligible = true, reason = (string?)null, storeAccountToken = StoreAccountToken });
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "private-mobile-cookie");

        using var prepared = await client.PostAsJsonAsync(Prefix + "prepare", new { provider, productId = "pack.10" });
        var preparation = await Body(prepared, HttpStatusCode.OK);
        Assert.Equal("repeatable", preparation.GetProperty("purchasePolicy").GetString());
        Assert.True(preparation.GetProperty("eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preparation.GetProperty("reason").ValueKind);
        Assert.Equal(StoreAccountToken, preparation.GetProperty("storeAccountToken").GetGuid());

        using var eligible = await client.PostAsJsonAsync(Prefix + "eligibility", new { provider, productId = "pack.10" });
        Assert.False((await Body(eligible, HttpStatusCode.OK)).TryGetProperty("storeAccountToken", out _));
        foreach (var expected in CompletionStatuses)
        {
            using var complete = await client.PostAsJsonAsync(Prefix + "complete", new { provider, productId = "pack.10", proof = "private-proof" });
            var result = await Body(complete, HttpStatusCode.OK);
            Assert.Equal(expected, result.GetProperty("status").GetString());
            Assert.Single(result.EnumerateObject());
        }
        Assert.Equal(new[] { "prepare", "eligibility", "complete", "complete" }, operations);
    }

    [Fact]
    public async Task IneligiblePreparation_DoesNotBlockRecoveryOrIssueAToken()
    {
        var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("prepare")
            ? Json(200, new { purchasePolicy = "once-per-customer", eligible = false, reason = "billing-purchase-policy-limit-reached", storeAccountToken = (string?)null })
            : Json(200, new { status = "already-delivered" })));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var prepared = await client.PostAsJsonAsync(Prefix + "prepare", new { provider = "app-store", productId = "pack.10" });
        var preparation = await Body(prepared, HttpStatusCode.OK);
        Assert.False(preparation.GetProperty("eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preparation.GetProperty("storeAccountToken").ValueKind);
        using var recovered = await client.PostAsJsonAsync(Prefix + "complete", new { provider = "app-store", productId = "pack.10", proof = "private-proof" });
        Assert.Equal("already-delivered", (await Body(recovered, HttpStatusCode.OK)).GetProperty("status").GetString());
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("eligibility")]
    [InlineData("complete")]
    public async Task AnonymousCaller_NeverReachesBilling(string operation)
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("must not call Billing"));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("test-anonymous", "true");
        using var response = await client.PostAsJsonAsync(Prefix + operation, new { provider = "app-store", productId = "pack.10" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(" ", 503)]
    public async Task MissingApplicationCustomer_NeverUsesClientOrDefaultIdentity(string? customer, int status)
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("must not call Billing"));
        await using var app = await Host(handler, _ => customer);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(Prefix + "prepare", new { provider = "app-store", productId = "pack.10" });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("prepare", "{\"provider\":\"app-store\",\"productId\":\"pack.10\",\"customerReference\":\"victim\"}")]
    [InlineData("complete", "{\"provider\":\"app-store\",\"productId\":\"pack.10\",\"proof\":\"private-proof\",\"environment\":\"production\"}")]
    [InlineData("complete", "{\"provider\":\"app-store\",\"productId\":\"pack.10\",\"proof\":\"private-proof\",\"quantity\":1000}")]
    [InlineData("complete", "{\"provider\":\"app-store\",\"productId\":\"pack.10\",\"proof\":\"private-proof\",\"offerGrantId\":\"not-supported\"}")]
    [InlineData("prepare", "{\"provider\":\"app-store\",\"productId\":\" \"}")]
    [InlineData("complete", "{\"provider\":\"app-store\",\"productId\":\"pack.10\"}")]
    [InlineData("prepare", "{broken-json")]
    [InlineData("prepare", "null")]
    public async Task InvalidOrServerOnlyInput_IsRejectedBeforeCallingBilling(string operation, string json)
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("must not call Billing"));
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsync(Prefix + operation, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(503, "billing-store-finalization-pending", true, true, false, 503)]
    [InlineData(409, "billing-purchase-policy-limit-reached", false, false, true, 409)]
    [InlineData(422, "billing-purchase-account-mismatch", false, false, false, 422)]
    [InlineData(404, "billing-product-not-found", false, false, false, 404)]
    [InlineData(429, "billing-unavailable", true, false, false, 429)]
    [InlineData(401, "billing-integration-unauthorized", false, false, false, 503)]
    [InlineData(403, "billing-integration-unauthorized", false, false, false, 503)]
    [InlineData(200, "billing-response-invalid", true, false, false, 503)]
    public async Task Errors_PreserveResolutionAndDelivery_WithoutExposingIntegrationAuthAsUserLogout(
        int upstreamStatus, string code, bool retryable, bool delivered, bool resolution, int bffStatus)
    {
        var handler = new Handler((_, _) =>
        {
            var response = Json(upstreamStatus, new { error = code, retryable, deliveryConfirmed = delivered, field = "private-proof" });
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(9));
            return Task.FromResult(response);
        });
        await using var app = await Host(handler);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(Prefix + "complete", new { provider = "app-store", productId = "pack.10", proof = "private-proof" });
        var error = await Body(response, (HttpStatusCode)bffStatus);
        Assert.Equal(code, error.GetProperty("error").GetString());
        Assert.Equal(retryable, error.GetProperty("retryable").GetBoolean());
        Assert.Equal(delivered, error.GetProperty("deliveryConfirmed").GetBoolean());
        Assert.Equal(resolution, error.GetProperty("resolutionRequired").GetBoolean());
        Assert.False(error.TryGetProperty("status", out _));
        if (upstreamStatus is not (401 or 403)) Assert.Equal(TimeSpan.FromSeconds(9), response.Headers.RetryAfter!.Delta);
        Assert.Equal(1, handler.Calls);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Credential, json);
        Assert.DoesNotContain(Customer, json);
        Assert.DoesNotContain("private-proof", json);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static HttpResponseMessage Json(int status, object body) => new((HttpStatusCode)status) { Content = JsonContent.Create(body) };

    private static async Task<WebApplication> Host(Handler handler, Func<HttpContext, string?>? customer = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("test-session").AddScheme<AuthenticationSchemeOptions, SessionAuthentication>("test-session", null);
        builder.Services.AddAuthorization();
        builder.Services.AddBullgateBillingClient(options =>
        {
            options.BaseAddress = new Uri("https://billing.test/");
            options.IntegrationCredential = Credential;
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBullgateOneTimePurchases(customer ?? (context => context.User.FindFirstValue(ClaimTypes.NameIdentifier)));
        app.MapBullgateOneTimeOffers(customer ?? (context => context.User.FindFirstValue(ClaimTypes.NameIdentifier)));
        await app.StartAsync();
        return app;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private sealed class SessionAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("test-anonymous")
            ? AuthenticateResult.NoResult()
            : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Customer)], Scheme.Name)), Scheme.Name)));
    }
}
