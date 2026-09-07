using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bullgate.Billing.AspNetCore.Tests;

public sealed class BillingSdkTests
{
    private static readonly string[] EligibilityRequestProperties =
        ["customerReference", "productId", "provider"];

    [Theory]
    [InlineData(503, "billing-store-finalization-pending", true, true)]
    [InlineData(503, "billing-store-finalization-pending", false, false)]
    [InlineData(503, "billing-provider-unavailable", true, false)]
    [InlineData(422, "billing-store-finalization-pending", true, false)]
    public async Task FinalizationFailure_ReportsConfirmedDeliveryOnlyForKnownContract(int status, string code, bool flag, bool expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(status, JsonSerializer.Serialize(new
        { error = code, retryable = true, deliveryConfirmed = flag }))));
        using var services = ClientServices(handler);
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() =>
            services.GetRequiredService<IBullgateBillingClient>().CompleteOneTimePurchaseAsync(Request()));
        Assert.Equal(expected, error.DeliveryConfirmed);
        Assert.True(error.Retryable);
        Assert.False(error.ResolutionRequired);
        Assert.Equal(1, handler.Calls);
        var client = services.GetRequiredService<IBullgateBillingClient>();
        Assert.False((await Assert.ThrowsAsync<BullgateBillingException>(() =>
            client.PrepareOneTimePurchaseAsync(new("customer", "google-play", "pack.10")))).DeliveryConfirmed);
        Assert.False((await Assert.ThrowsAsync<BullgateBillingException>(() =>
            client.CheckOneTimePurchaseEligibilityAsync(new("customer", "google-play", "pack.10")))).DeliveryConfirmed);
    }

    [Theory]
    [InlineData("repeatable", true)]
    [InlineData("once-per-customer", true)]
    [InlineData("once-per-customer", false)]
    public async Task Preparation_SendsOnlyCustomerAndProduct_AndReturnsBindingOnlyWhenEligible(string policy, bool eligible)
    {
        var token = Guid.NewGuid();
        var handler = new Handler(async (request, cancellationToken) =>
        {
            Assert.Equal("https://billing.test/base/v1/purchases/one-time/prepare", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(3, body.EnumerateObject().Count());
            Assert.Equal("customer", body.GetProperty("customerReference").GetString());
            Assert.Equal("app-store", body.GetProperty("provider").GetString());
            Assert.Equal("pack.10", body.GetProperty("productId").GetString());
            return Json(200, JsonSerializer.Serialize(new
            {
                purchasePolicy = policy,
                eligible,
                reason = eligible ? null : "billing-purchase-policy-limit-reached",
                storeAccountToken = eligible ? token.ToString("D") : null
            }));
        });
        using var services = ClientServices(handler);
        var result = await services.GetRequiredService<IBullgateBillingClient>().PrepareOneTimePurchaseAsync(new("customer", "app-store", "pack.10"));
        Assert.Equal(eligible, result.Eligible);
        Assert.Equal(eligible ? token : (Guid?)null, result.StoreAccountToken);
        Assert.Equal(policy == "repeatable" ? BullgateOneTimePurchasePolicy.Repeatable : BullgateOneTimePurchasePolicy.OncePerCustomer, result.PurchasePolicy);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"purchasePolicy\":\"repeatable\",\"eligible\":true}")]
    [InlineData("{\"purchasePolicy\":\"repeatable\",\"eligible\":true,\"storeAccountToken\":\"bad\"}")]
    [InlineData("{\"purchasePolicy\":\"repeatable\",\"eligible\":true,\"storeAccountToken\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"purchasePolicy\":\"once-per-customer\",\"eligible\":false,\"reason\":\"billing-purchase-policy-limit-reached\",\"storeAccountToken\":\"e4e33544-ff45-42ea-99b3-d5c46ca6db9b\"}")]
    [InlineData("{\"purchasePolicy\":\"repeatable\",\"eligible\":false,\"reason\":\"billing-purchase-policy-limit-reached\"}")]
    [InlineData("{\"purchasePolicy\":\"once-per-customer\",\"eligible\":false,\"reason\":\"unknown\"}")]
    public async Task Preparation_RejectsIncompleteOrInconsistentResponses(string body)
    {
        using var services = ClientServices(new Handler((_, _) => Task.FromResult(Json(200, body))));
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() => services.GetRequiredService<IBullgateBillingClient>()
            .PrepareOneTimePurchaseAsync(new("customer", "app-store", "pack.10")));
        Assert.Equal("billing-response-invalid", error.Error);
        Assert.True(error.Retryable);
    }

    [Theory]
    [InlineData("delivered", BullgatePurchaseCompletion.Delivered)]
    [InlineData("already-delivered", BullgatePurchaseCompletion.AlreadyDelivered)]
    public async Task Client_SendsOnlyCompletionContractAndParsesKnownSuccess(string status, BullgatePurchaseCompletion expected)
    {
        var handler = new Handler(async (request, cancellationToken) =>
        {
            Assert.Equal("https://billing.test/base/v1/purchases/one-time/complete", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("customerReference", json);
            Assert.DoesNotContain("environment", json);
            return Json(200, $"{{\"status\":\"{status}\"}}");
        });
        using var services = ClientServices(handler);
        var result = await services.GetRequiredService<IBullgateBillingClient>().CompleteOneTimePurchaseAsync(Request());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(503, "{\"error\":\"billing-provider-unavailable\",\"retryable\":true}", true)]
    [InlineData(409, "{\"error\":\"billing-purchase-already-owned\"}", false)]
    [InlineData(422, "{\"error\":\"billing-product-mismatch\",\"retryable\":false}", false)]
    [InlineData(401, "", false)]
    [InlineData(403, "", false)]
    [InlineData(429, "not-json", true)]
    [InlineData(200, "{\"status\":\"pending\"}", true)]
    [InlineData(200, "not-json", true)]
    [InlineData(302, "", true)]
    [InlineData(422, "{\"retryable\":false}", true)]
    public async Task Client_ReportsFailureAndDoesNotRetryAutomatically(int status, string body, bool retryable)
    {
        foreach (var operation in new[] { "complete", "eligibility", "prepare" })
        {
            var handler = new Handler((_, _) =>
            {
                var response = Json(status, body);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(9));
                return Task.FromResult(response);
            });
            using var services = ClientServices(handler);
            var client = services.GetRequiredService<IBullgateBillingClient>();
            Task InvokeAsync() => operation switch
            {
                "prepare" => client.PrepareOneTimePurchaseAsync(new("customer", "app-store", "pack.10")),
                "eligibility" => client.CheckOneTimePurchaseEligibilityAsync(EligibilityRequest()),
                _ => client.CompleteOneTimePurchaseAsync(Request()),
            };
            var exception = await Assert.ThrowsAsync<BullgateBillingException>(InvokeAsync);
            Assert.Equal(retryable, exception.Retryable);
            Assert.Equal(1, handler.Calls);
            if (status == 503) Assert.Equal(TimeSpan.FromSeconds(9), exception.RetryAfter);
            Assert.DoesNotContain("private-proof", exception.ToString());
        }
    }

    [Fact]
    public async Task Client_DistinguishesTimeoutFromCallerCancellation()
    {
        using var services = ClientServices(new Handler((_, _) => throw new OperationCanceledException()));
        var client = services.GetRequiredService<IBullgateBillingClient>();
        Assert.True((await Assert.ThrowsAsync<BullgateBillingException>(() => client.CompleteOneTimePurchaseAsync(Request()))).Retryable);
        Assert.Equal("billing-timeout", (await Assert.ThrowsAsync<BullgateBillingException>(() =>
            client.CheckOneTimePurchaseEligibilityAsync(EligibilityRequest()))).Error);
        Assert.Equal("billing-timeout", (await Assert.ThrowsAsync<BullgateBillingException>(() =>
            client.PrepareOneTimePurchaseAsync(new("customer", "app-store", "pack.10")))).Error);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PrepareOneTimePurchaseAsync(new("customer", "app-store", "pack.10"), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteOneTimePurchaseAsync(Request(), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CheckOneTimePurchaseEligibilityAsync(EligibilityRequest(), cancelled.Token));
    }

    [Theory]
    [InlineData("repeatable", true, BullgateOneTimePurchasePolicy.Repeatable)]
    [InlineData("once-per-customer", true, BullgateOneTimePurchasePolicy.OncePerCustomer)]
    [InlineData("once-per-customer", false, BullgateOneTimePurchasePolicy.OncePerCustomer)]
    public async Task Eligibility_SendsOnlyReadContractAndReturnsTypedPolicy(string policy, bool eligible, BullgateOneTimePurchasePolicy expected)
    {
        var reason = eligible ? null : "billing-purchase-policy-limit-reached";
        var handler = new Handler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://billing.test/base/v1/purchases/one-time/eligibility", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("bgbc_test", request.Headers.Authorization.Parameter);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(EligibilityRequestProperties,
                body.EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal("customer", body.GetProperty("customerReference").GetString());
            Assert.Equal("fake-store", body.GetProperty("provider").GetString());
            Assert.Equal("pack.10", body.GetProperty("productId").GetString());
            return Json(200, JsonSerializer.Serialize(new { purchasePolicy = policy, eligible, reason }));
        });
        using var services = ClientServices(handler);
        var result = await services.GetRequiredService<IBullgateBillingClient>().CheckOneTimePurchaseEligibilityAsync(EligibilityRequest());
        Assert.Equal(expected, result.PurchasePolicy);
        Assert.Equal(eligible, result.Eligible);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"eligible\":true}")]
    [InlineData("{\"purchasePolicy\":\"unknown\",\"eligible\":true}")]
    [InlineData("{\"purchasePolicy\":\"repeatable\"}")]
    [InlineData("{\"purchasePolicy\":\"repeatable\",\"eligible\":\"true\"}")]
    [InlineData("{\"purchasePolicy\":\"once-per-customer\",\"eligible\":false}")]
    [InlineData("{\"purchasePolicy\":\"once-per-customer\",\"eligible\":false,\"reason\":\"private response text\"}")]
    [InlineData("{\"purchasePolicy\":\"once-per-customer\",\"eligible\":true,\"reason\":\"billing-purchase-policy-limit-reached\"}")]
    public async Task Eligibility_DoesNotInferPermissionFromIncompleteOrUnknownResponses(string body)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(200, body)));
        using var services = ClientServices(handler);
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() =>
            services.GetRequiredService<IBullgateBillingClient>().CheckOneTimePurchaseEligibilityAsync(EligibilityRequest()));
        Assert.Equal("billing-response-invalid", error.Error);
        Assert.True(error.Retryable);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("private response text", error.ToString());
    }

    [Theory]
    [InlineData(409, "billing-purchase-policy-limit-reached", true, false, true)]
    [InlineData(409, "billing-purchase-policy-limit-reached", null, false, true)]
    [InlineData(409, "billing-purchase-policy-limit-reached", false, false, true)]
    [InlineData(409, "billing-other-resolution", true, false, true)]
    [InlineData(422, "billing-purchase-product-mismatch", false, false, false)]
    [InlineData(503, "billing-provider-unavailable", null, true, false)]
    public async Task Client_PreservesResolutionRequirementIndependentlyOfRetryable(
        int status, string code, bool? resolutionRequired, bool retryable, bool expectedResolution)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(status, JsonSerializer.Serialize(new
        {
            error = code,
            retryable,
            resolutionRequired,
            field = "productId",
        }))));
        using var services = ClientServices(handler);
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() =>
            services.GetRequiredService<IBullgateBillingClient>().CompleteOneTimePurchaseAsync(Request()));
        Assert.Equal(expectedResolution, error.ResolutionRequired);
        Assert.Equal(retryable, error.Retryable);
        Assert.Equal(code, error.Error);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("productId", error.Field);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("private-proof", error.ToString());
    }

    [Fact]
    public async Task Eligibility_ValidatesLocalInputBeforeHttpAndReportsNetworkFailure()
    {
        var handler = new Handler((_, _) => throw new HttpRequestException("private-network-detail"));
        using var services = ClientServices(handler);
        var client = services.GetRequiredService<IBullgateBillingClient>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CheckOneTimePurchaseEligibilityAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.PrepareOneTimePurchaseAsync(null!));
        foreach (var invalid in new[]
        {
            EligibilityRequest() with { CustomerReference = " " },
            EligibilityRequest() with { Provider = "" },
            EligibilityRequest() with { ProductId = "" },
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => client.CheckOneTimePurchaseEligibilityAsync(invalid));
            await Assert.ThrowsAsync<ArgumentException>(() => client.PrepareOneTimePurchaseAsync(new(invalid.CustomerReference, invalid.Provider, invalid.ProductId)));
        }
        Assert.Equal(0, handler.Calls);
        var error = await Assert.ThrowsAsync<BullgateBillingException>(() => client.CheckOneTimePurchaseEligibilityAsync(EligibilityRequest()));
        Assert.Equal("billing-unavailable", error.Error);
        Assert.True(error.Retryable);
        Assert.False(error.ResolutionRequired);
        Assert.DoesNotContain("private-network-detail", error.ToString());
        var preparationError = await Assert.ThrowsAsync<BullgateBillingException>(() => client.PrepareOneTimePurchaseAsync(new("customer", "app-store", "pack.10")));
        Assert.Equal("billing-unavailable", preparationError.Error);
        Assert.DoesNotContain("private-network-detail", preparationError.ToString());
    }

    [Theory]
    [InlineData(Path)]
    [InlineData(ReversePath)]
    public async Task Receiver_AuthenticatesSeparateCredentialAndDerivesEnvironment(string path)
    {
        var first = BullgateDeliveryCredential.Generate();
        var second = BullgateDeliveryCredential.Generate();
        var app = new RecordingApplication();
        await using var host = await ReceiverAsync(app, [first, second]);
        using var client = host.GetTestClient();
        var deliveryId = Guid.NewGuid();
        var command = ReceiverRequest(path, deliveryId, 2);
        command["environmentKey"] = "foreign";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, command)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bgbc_wrong-direction");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, command)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BullgateDeliveryCredential.Generate());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, command)).StatusCode);
        foreach (var credential in new[] { first, second })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            var response = await client.PostAsJsonAsync(path, command);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(deliveryId.ToString(), await response.Content.ReadAsStringAsync());
            Assert.Equal("example-development", app.Last!.EnvironmentKey);
            Assert.Equal(path == ReversePath, app.ReverseCalled);
        }
    }

    [Theory]
    [InlineData(Path)]
    [InlineData(ReversePath)]
    public async Task Receiver_RejectsInvalidInputAndSanitizesUnexpectedExceptions(string path)
    {
        var token = BullgateDeliveryCredential.Generate();
        var application = new RecordingApplication { Failure = new InvalidOperationException("private-data") };
        await using var host = await ReceiverAsync(application, [token]);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync(path, ReceiverRequest(path, Guid.NewGuid(), 1));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private-data", await response.Content.ReadAsStringAsync());
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter!.Delta);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, ReceiverRequest(path, Guid.NewGuid(), 0))).StatusCode);
        application.Failure = new BullgateEntitlementRejectedException("entitlement-delivery-conflict");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(path, ReceiverRequest(path, Guid.NewGuid(), 1))).StatusCode);
    }

    [Fact]
    public void Client_RequiresExplicitOptInForHttp()
    {
        var services = new ServiceCollection();
        services.AddBullgateBillingClient(options =>
        {
            options.BaseAddress = new Uri("http://billing.test/");
            options.IntegrationCredential = "bgbc_test";
        });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => provider.GetRequiredService<IBullgateBillingClient>());
    }

    private const string Path = "/bullgate/billing/v1/entitlements/deliver";
    private const string ReversePath = "/bullgate/billing/v1/entitlements/reverse";
    private static Dictionary<string, object> ReceiverRequest(string path, Guid deliveryId, decimal quantity) => new()
    {
        [path == ReversePath ? "originalDeliveryId" : "deliveryId"] = deliveryId,
        ["customerReference"] = "customer", ["entitlementKey"] = "units", ["quantity"] = quantity,
    };

    [Theory]
    [InlineData(Path, ReversePath)]
    [InlineData(ReversePath, Path)]
    public async Task ReceiverDoesNotConfuseDeliveryAndReversal(string destination, string payloadKind)
    {
        var token = BullgateDeliveryCredential.Generate();
        var application = new RecordingApplication();
        await using var host = await ReceiverAsync(application, [token]);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(destination, ReceiverRequest(payloadKind, Guid.NewGuid(), 10))).StatusCode);
        Assert.Null(application.Last);
    }
    private static BullgateOneTimePurchaseRequest Request() => new("customer", "fake-store", "pack.10", "private-proof");
    private static BullgateOneTimePurchaseEligibilityRequest EligibilityRequest() => new("customer", "fake-store", "pack.10");
    private static HttpResponseMessage Json(int status, string json) => new((HttpStatusCode)status)
    { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    private static ServiceProvider ClientServices(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddBullgateBillingClient(options =>
        {
            options.BaseAddress = new Uri("https://billing.test/base/");
            options.IntegrationCredential = "bgbc_test";
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
    private static async Task<WebApplication> ReceiverAsync(RecordingApplication application, string[] tokens)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddBullgateEntitlementReceiver<RecordingApplication>(options =>
        {
            options.EnvironmentKey = "example-development";
            options.AcceptedCredentials = tokens;
        });
        builder.Services.AddSingleton<IBullgateEntitlementApplication>(application);
        var app = builder.Build();
        app.MapBullgateEntitlementDelivery();
        app.MapBullgateEntitlementReversal();
        await app.StartAsync();
        return app;
    }
    public sealed class RecordingApplication : IBullgateEntitlementApplication
    {
        public BullgateEntitlementDelivery? Last { get; private set; }
        public Exception? Failure { get; set; }
        public bool ReverseCalled { get; private set; }
        public Task<BullgateEntitlementResult> ReverseAsync(BullgateEntitlementReversal reversal, CancellationToken cancellationToken)
        {
            ReverseCalled = true;
            return DeliverAsync(new(reversal.DeliveryId, reversal.EnvironmentKey, reversal.CustomerReference,
                reversal.EntitlementKey, reversal.Quantity), cancellationToken);
        }
        public Task<BullgateEntitlementResult> DeliverAsync(BullgateEntitlementDelivery delivery, CancellationToken cancellationToken)
        {
            Last = delivery;
            if (Failure is not null) throw Failure;
            return Task.FromResult(BullgateEntitlementResult.Applied);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return send(request, cancellationToken); }
    }
}
