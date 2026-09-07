using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bullgate.Access.AspNetCore.Tests;

public sealed class BullgateAccessClientTests
{
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Register_SendsTheIntegrationCredentialAndParsesTheSession()
    {
        HttpRequestMessage? captured = null;
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = Json(
                    $$"""
                    {
                      "identityId": "{{identityId}}",
                      "email": "person@example.com",
                      "phone": "+5511999999999",
                      "phoneVerifiedAt": "2026-09-02T12:00:00Z",
                      "sessionToken": "bgs_secret",
                      "sessionExpiresAt": "2030-01-01T00:00:00Z",
                      "sessionPurpose": "registration",
                      "isNew": true,
                      "hasPassword": true,
                      "hasGoogle": false,
                      "hasApple": false
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.RegisterAsync(
            "person@example.com",
            "password-123");

        Assert.Equal(identityId, result.IdentityId);
        Assert.True(result.IsNew);
        Assert.Equal(BullgateSessionPurpose.Registration, result.Purpose);
        Assert.Equal("+5511999999999", result.Phone);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-02T12:00:00Z"),
            result.PhoneVerifiedAt);
        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("bgic_test.secret", captured.Headers.Authorization?.Parameter);
        Assert.Equal("/v1/auth/register", captured.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task Login_PreservesTheStableDomainError()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = Json("""{"error":"invalid-credentials"}"""),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessRejectedException>(
            () => client.LoginAsync("person@example.com", "wrong-password"));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        Assert.Equal("invalid-credentials", exception.Error);
    }

    [Theory]
    [InlineData("google", "/v1/auth/google")]
    [InlineData("apple", "/v1/auth/apple")]
    public async Task SocialAuthentication_UsesProviderSpecificCredentialContract(
        string provider,
        string expectedPath)
    {
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedAuthorization = null;
        string? capturedJson = null;
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri?.AbsolutePath;
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = IssuedSessionJson(identityId),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = provider switch
        {
            "google" => await client.GoogleAsync("google-id-token", "google-access-token"),
            "apple" => await client.AppleAsync("apple-identity-token"),
            _ => throw new InvalidOperationException("Unsupported test provider."),
        };

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal(expectedPath, capturedPath);
        Assert.Equal("Bearer bgic_test.secret", capturedAuthorization);
        Assert.NotNull(capturedJson);
        using var requestBody = JsonDocument.Parse(capturedJson);
        var propertyNames = requestBody.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (provider == "google")
        {
            Assert.Equal(["accessToken", "idToken"], propertyNames);
            Assert.Equal(
                "google-id-token",
                requestBody.RootElement.GetProperty("idToken").GetString());
            Assert.Equal(
                "google-access-token",
                requestBody.RootElement.GetProperty("accessToken").GetString());
        }
        else
        {
            Assert.Equal(["identityToken"], propertyNames);
            Assert.Equal(
                "apple-identity-token",
                requestBody.RootElement.GetProperty("identityToken").GetString());
        }

        Assert.Equal(identityId, result.IdentityId);
        Assert.Equal(BullgateSessionPurpose.Product, result.Purpose);
    }

    [Fact]
    public async Task Introspection_RequiresAnExplicitActiveFlag()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("{}"),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.IntrospectAsync("bgs_existing"));
    }

    [Fact]
    public async Task Introspection_ReturnsNullOnlyForExplicitInactiveSession()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("""{"active":false}"""),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var session = await client.IntrospectAsync("bgs_expired");

        Assert.Null(session);
    }

    [Fact]
    public async Task Introspection_RejectsIncompleteActiveSession()
    {
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    $$"""
                    {
                      "active": true,
                      "identityId": "{{identityId}}",
                      "email": "person@example.com",
                      "expiresAt": "2030-01-01T00:00:00Z",
                      "sessionPurpose": "product"
                    }
                    """),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.IntrospectAsync("bgs_existing"));

        Assert.Contains("incomplete active session", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Introspection_ParsesTheIdentityPhone()
    {
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    $$"""
                    {
                      "active": true,
                      "identityId": "{{identityId}}",
                      "sessionId": "{{sessionId}}",
                      "email": "person@example.com",
                      "phone": "+5511999999999",
                      "phoneVerifiedAt": "2026-09-02T12:00:00Z",
                      "expiresAt": "2030-01-01T00:00:00Z",
                      "sessionPurpose": "product",
                      "hasPassword": true,
                      "hasGoogle": false,
                      "hasApple": false
                    }
                    """),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var session = await client.IntrospectAsync("bgs_existing");

        Assert.NotNull(session);
        Assert.Equal("+5511999999999", session.Phone);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-02T12:00:00Z"),
            session.PhoneVerifiedAt);
    }

    [Theory]
    [InlineData("application/json", "{}")]
    [InlineData("application/json", "{")]
    [InlineData("text/plain", "upstream failure")]
    public async Task MalformedDomainError_IsTreatedAsUnavailable(
        string contentType,
        string responseBody)
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, contentType),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.LoginAsync("person@example.com", "wrong-password"));

        Assert.Contains("operational status 401", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("application/json", "null", "empty response")]
    [InlineData("application/json", "{", "invalid JSON")]
    [InlineData("text/plain", "not JSON", "invalid JSON")]
    public async Task InvalidSuccessPayload_IsTreatedAsUnavailable(
        string contentType,
        string responseBody,
        string expectedMessage)
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, contentType),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.LoginAsync("person@example.com", "password-123"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssuedSessionResponse_RejectsMissingBearerToken()
    {
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    $$"""
                    {
                      "identityId": "{{identityId}}",
                      "email": "person@example.com",
                      "sessionToken": "",
                      "sessionExpiresAt": "2030-01-01T00:00:00Z",
                      "sessionPurpose": "product",
                      "isNew": false,
                      "hasPassword": true,
                      "hasGoogle": false,
                      "hasApple": false
                    }
                    """),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.LoginAsync("person@example.com", "password-123"));

        Assert.Contains("incomplete session response", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpRequestFailure_IsTreatedAsUnavailable()
    {
        var transportFailure = new HttpRequestException("Network unavailable in test.");
        var handler = new DelegateHandler(_ => throw transportFailure);
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.LoginAsync("person@example.com", "password-123"));

        Assert.Same(transportFailure, exception.InnerException);
        Assert.Contains("could not be reached", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutIsUnavailableButCallerCancellationIsPreserved()
    {
        var timeoutHandler = new DelegateHandler(
            _ => throw new OperationCanceledException("Timeout in test."));
        await using (var timeoutServices = CreateServices(timeoutHandler))
        {
            var timeoutClient = timeoutServices.GetRequiredService<IBullgateAccessClient>();
            var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
                () => timeoutClient.LoginAsync("person@example.com", "password-123"));

            Assert.IsType<OperationCanceledException>(exception.InnerException);
            Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancellationHandler = new DelegateHandler(
            _ => throw new OperationCanceledException("Caller canceled in test."));
        await using var cancellationServices = CreateServices(cancellationHandler);
        var cancellationClient = cancellationServices
            .GetRequiredService<IBullgateAccessClient>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancellationClient.LoginAsync(
                "person@example.com",
                "password-123",
                cancellation.Token));
    }

    [Fact]
    public async Task GetEmail_UsesTheServerSideIdentityEndpointAndParsesEmail()
    {
        HttpRequestMessage? captured = null;
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("""{"email":"normalized@example.com"}"""),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var email = await client.GetEmailAsync(identityId);

        Assert.Equal("normalized@example.com", email);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal(
            $"/v1/identities/{identityId:D}/email",
            captured.RequestUri?.AbsolutePath);
        Assert.Null(captured.Content);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("bgic_test.secret", captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetEmail_PreservesIdentityNotFound()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = Json("""{"error":"identity-not-found"}"""),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessRejectedException>(
            () => client.GetEmailAsync(Guid.CreateVersion7()));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("identity-not-found", exception.Error);
    }

    [Fact]
    public async Task GetPhone_UsesTheServerSideIdentityEndpointAndParsesPhone()
    {
        HttpRequestMessage? captured = null;
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    """
                    {
                      "phone": "+5511999999999",
                      "verifiedAt": "2026-09-02T12:00:00Z"
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.GetPhoneAsync(identityId);

        Assert.Equal("+5511999999999", result.Phone);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-02T12:00:00Z"),
            result.VerifiedAt);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal(
            $"/v1/identities/{identityId:D}/phone",
            captured.RequestUri?.AbsolutePath);
        Assert.Null(captured.Content);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("bgic_test.secret", captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetPhone_PreservesIdentityNotFound()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = Json("""{"error":"identity-not-found"}"""),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessRejectedException>(
            () => client.GetPhoneAsync(Guid.CreateVersion7()));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("identity-not-found", exception.Error);
    }

    [Fact]
    public async Task ApplicationClientConfiguration_UsesTheStableKeyAndParsesPublicJson()
    {
        HttpRequestMessage? captured = null;
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    """
                    {
                      "configurationVersion": 3,
                      "common": {
                        "supportUrl": "https://example.test/support"
                      },
                      "client": {
                        "key": "android-internal",
                        "name": "Android internal",
                        "platform": "android",
                        "applicationId": "app.example.test",
                        "signingIdentity": "sha256:certificate",
                        "smsRetrieverAppHash": "AbCdEfGhIJK",
                        "configuration": {
                          "featureFlags": { "wordGame": true }
                        }
                      }
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.GetApplicationClientConfigurationAsync(
            "android-internal");

        Assert.Equal(3, result.ConfigurationVersion);
        Assert.Equal(
            "https://example.test/support",
            result.Common.GetProperty("supportUrl").GetString());
        Assert.True(
            result.Client
                .Configuration
                .GetProperty("featureFlags")
                .GetProperty("wordGame")
                .GetBoolean());
        Assert.Equal("android-internal", result.Client.Key);
        Assert.Equal("Android internal", result.Client.Name);
        Assert.Equal("android", result.Client.Platform);
        Assert.Equal("app.example.test", result.Client.ApplicationId);
        Assert.Equal("sha256:certificate", result.Client.SigningIdentity);
        Assert.Equal("AbCdEfGhIJK", result.Client.SmsRetrieverAppHash);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal(
            "/v1/config/application-clients/android-internal",
            captured.RequestUri?.AbsolutePath);
        Assert.Null(captured.Content);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("bgic_test.secret", captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ApplicationClientConfiguration_AllowsNullableWebMetadata()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    """
                    {
                      "configurationVersion": 1,
                      "common": {},
                      "client": {
                        "key": "web-development",
                        "name": "Web development",
                        "platform": "web",
                        "applicationId": null,
                        "signingIdentity": null,
                        "smsRetrieverAppHash": null,
                        "configuration": {}
                      }
                    }
                    """),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.GetApplicationClientConfigurationAsync(
            "web-development");

        Assert.Equal("web", result.Client.Platform);
        Assert.Null(result.Client.ApplicationId);
        Assert.Null(result.Client.SigningIdentity);
        Assert.Null(result.Client.SmsRetrieverAppHash);
        Assert.Equal(JsonValueKind.Object, result.Client.Configuration.ValueKind);
    }

    [Fact]
    public async Task ApplicationClientOperations_RejectBlankKeysBeforeSending()
    {
        var sends = 0;
        var handler = new DelegateHandler(_ =>
        {
            sends++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetApplicationClientConfigurationAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.RequestPasswordRecoveryByPhoneAsync(
                "+5511999999999",
                "\t"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.StartFlowAsync(
                Guid.CreateVersion7(),
                [1],
                "continueRegistration",
                "",
                sessionToken: null));

        Assert.Equal(0, sends);
    }

    [Fact]
    public async Task RevokeCurrentSession_SendsOnlyTheOpaqueSessionToken()
    {
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedAuthorization = null;
        string? capturedJson = null;
        var handler = new DelegateHandler(request =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri?.AbsolutePath;
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        await client.RevokeCurrentSessionAsync("bgs_existing");

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/v1/auth/session/revoke", capturedPath);
        Assert.Equal("Bearer bgic_test.secret", capturedAuthorization);
        Assert.NotNull(capturedJson);
        using var requestBody = JsonDocument.Parse(capturedJson);
        Assert.Equal(
            ["sessionToken"],
            requestBody.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            "bgs_existing",
            requestBody.RootElement.GetProperty("sessionToken").GetString());
    }

    [Fact]
    public async Task GetFlow_SendsCapabilityOnlyInItsHeaderAndParsesSnapshot()
    {
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedAuthorization = null;
        string? capturedCapability = null;
        bool? hadContent = null;
        var flowId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri?.AbsolutePath;
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedCapability = Assert.Single(
                request.Headers.GetValues("Bullgate-Flow-Capability"));
            hadContent = request.Content is not null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ActiveFlowJson(flowId),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.GetFlowAsync(flowId, "bgf_opaque");

        Assert.Equal(HttpMethod.Get, capturedMethod);
        Assert.Equal($"/v1/access/flows/{flowId:D}", capturedPath);
        Assert.Equal("Bearer bgic_test.secret", capturedAuthorization);
        Assert.Equal("bgf_opaque", capturedCapability);
        Assert.False(hadContent);
        Assert.Equal(flowId, result.Snapshot.FlowId);
        Assert.Equal("continueRegistration", result.Snapshot.Intent);
        Assert.Null(result.IssuedSession);
    }

    [Theory]
    [InlineData("missing-capability", "incomplete access flow response")]
    [InlineData("missing-actions", "incomplete access flow response")]
    [InlineData("incomplete-issued-session", "incomplete access flow session")]
    public async Task GetFlow_RejectsIncompleteProtocolState(
        string fault,
        string expectedMessage)
    {
        var flowId = Guid.CreateVersion7();
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        object? actions = fault == "missing-actions" ? null : Array.Empty<object>();
        object? issuedSession = fault == "incomplete-issued-session"
            ? new
            {
                identityId,
                sessionId,
                sessionToken = (string?)null,
                expiresAt = DateTimeOffset.Parse("2030-01-01T00:00:00Z"),
                purpose = "product",
            }
            : null;
        var payload = JsonSerializer.Serialize(
            new
            {
                flowCapability = fault == "missing-capability" ? " " : "bgf_opaque",
                snapshot = new
                {
                    protocolVersion = 1,
                    flowId,
                    revision = 1,
                    intent = "continueRegistration",
                    status = "active",
                    expiresAt = DateTimeOffset.Parse("2030-01-01T00:10:00Z"),
                    step = new { type = "collectProfile" },
                    actions,
                },
                issuedSession,
            },
            WebJsonOptions);
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(payload),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.GetFlowAsync(flowId, "bgf_opaque"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlowAction_SendsTheOpaqueCapabilityAndParsesTheIssuedSession()
    {
        HttpRequestMessage? captured = null;
        var flowId = Guid.CreateVersion7();
        var actionId = Guid.CreateVersion7();
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    $$"""
                    {
                      "flowCapability": "bgf_opaque",
                      "snapshot": {
                        "protocolVersion": 1,
                        "flowId": "{{flowId}}",
                        "revision": 2,
                        "intent": "continueRegistration",
                        "status": "completed",
                        "expiresAt": "2030-01-01T00:00:00Z",
                        "actions": [],
                        "result": { "type": "registrationCompleted" }
                      },
                      "issuedSession": {
                        "identityId": "{{identityId}}",
                        "sessionId": "{{sessionId}}",
                        "sessionToken": "bgs_product",
                        "expiresAt": "2030-02-01T00:00:00Z",
                        "purpose": "product"
                      }
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.ActOnFlowAsync(
            flowId,
            "bgf_opaque",
            Guid.CreateVersion7(),
            1,
            new BullgateAccessFlowAction(
                actionId,
                "skipRegistration",
                JsonSerializer.SerializeToElement(new { })));

        Assert.NotNull(captured);
        Assert.Equal(
            "bgf_opaque",
            Assert.Single(captured.Headers.GetValues("Bullgate-Flow-Capability")));
        Assert.Equal($"/v1/access/flows/{flowId:D}/actions", captured.RequestUri?.AbsolutePath);
        Assert.Equal(BullgateSessionPurpose.Product, result.IssuedSession?.Purpose);
        Assert.Equal("bgs_product", result.IssuedSession?.SessionToken);
    }

    [Fact]
    public async Task FlowAction_PreservesVerificationDeliveryUnavailable()
    {
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = Json(
                    """{"error":"verification-delivery-unavailable"}"""),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessRejectedException>(
            () => client.ActOnFlowAsync(
                Guid.CreateVersion7(),
                "bgf_opaque",
                Guid.CreateVersion7(),
                1,
                new BullgateAccessFlowAction(
                    Guid.CreateVersion7(),
                    "requestPhoneVerification",
                    JsonSerializer.SerializeToElement(new { phone = "+5511999999999" }))));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal("verification-delivery-unavailable", exception.Error);
    }

    [Fact]
    public async Task PasswordRecovery_UsesTheFinalEndpointsAndParsesPhoneResponses()
    {
        var requests = new List<(string Path, string? Body)>();
        const string applicationClientKey = "android-internal";
        var handler = new DelegateHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            requests.Add((
                path,
                request.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));

            return path switch
            {
                "/v1/auth/password/recovery/email" =>
                    new HttpResponseMessage(HttpStatusCode.Accepted),
                "/v1/auth/password/recovery/phone" =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = Json(
                            """
                            {
                              "expiresAt": "2030-01-01T00:10:00Z",
                              "resendAvailableAt": "2030-01-01T00:01:00Z"
                            }
                            """),
                    },
                "/v1/auth/password/recovery/phone/confirm" =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = Json(
                            """
                            {
                              "token": "reset-token",
                              "expiresAt": "2030-01-01T01:00:00Z"
                            }
                            """),
                    },
                "/v1/auth/password/recovery/reset" =>
                    new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        await client.RequestPasswordRecoveryByEmailAsync("person@example.com");
        var challenge = await client.RequestPasswordRecoveryByPhoneAsync(
            "+5511999999999",
            applicationClientKey);
        var resetToken = await client.ConfirmPasswordRecoveryByPhoneAsync(
            "+5511999999999",
            "123456");
        await client.ResetPasswordAsync("reset-token", "new-password-456");

        Assert.Equal(
            DateTimeOffset.Parse("2030-01-01T00:10:00Z"),
            challenge.ExpiresAt);
        Assert.Equal(
            DateTimeOffset.Parse("2030-01-01T00:01:00Z"),
            challenge.ResendAvailableAt);
        Assert.Equal("reset-token", resetToken.Token);
        Assert.Equal(
            [
                "/v1/auth/password/recovery/email",
                "/v1/auth/password/recovery/phone",
                "/v1/auth/password/recovery/phone/confirm",
                "/v1/auth/password/recovery/reset",
            ],
            requests.Select(request => request.Path));

        using var phoneBody = JsonDocument.Parse(requests[1].Body!);
        Assert.Equal(
            applicationClientKey,
            phoneBody.RootElement.GetProperty("applicationClientKey").GetString());
        using var confirmBody = JsonDocument.Parse(requests[2].Body!);
        Assert.Equal("123456", confirmBody.RootElement.GetProperty("code").GetString());
        using var resetBody = JsonDocument.Parse(requests[3].Body!);
        Assert.Equal(
            "new-password-456",
            resetBody.RootElement.GetProperty("newPassword").GetString());
    }

    [Fact]
    public async Task CurrentIdentityOperation_SendsTheSessionTokenAndParsesAuthenticators()
    {
        HttpRequestMessage? captured = null;
        string? requestJson = null;
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            requestJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    $$"""
                    {
                      "identityId": "{{identityId}}",
                      "sessionId": "{{sessionId}}",
                      "email": "person@example.com",
                      "phone": "+5511999999999",
                      "phoneVerifiedAt": "2026-09-02T12:00:00Z",
                      "expiresAt": "2030-01-01T00:00:00Z",
                      "sessionPurpose": "product",
                      "hasPassword": true,
                      "hasGoogle": true,
                      "googleEmail": "google@example.com",
                      "hasApple": false
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.ChangePasswordAsync(
            "bgs_existing",
            "password-123",
            "new-password-456");

        Assert.Equal("/v1/account/password", captured?.RequestUri?.AbsolutePath);
        Assert.NotNull(requestJson);
        using var requestBody = JsonDocument.Parse(requestJson);
        Assert.Equal(
            "bgs_existing",
            requestBody.RootElement.GetProperty("sessionToken").GetString());
        Assert.Equal(identityId, result.IdentityId);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("+5511999999999", result.Phone);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-02T12:00:00Z"),
            result.PhoneVerifiedAt);
        Assert.True(result.HasPassword);
        Assert.True(result.HasGoogle);
        Assert.Equal("google@example.com", result.GoogleEmail);
    }

    [Fact]
    public async Task ChangeEmail_UsesTheDirectUpdateEndpointAndReturnsTheUpdatedSession()
    {
        HttpRequestMessage? captured = null;
        string? requestJson = null;
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            requestJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(
                    $$"""
                    {
                      "identityId": "{{identityId}}",
                      "sessionId": "{{sessionId}}",
                      "email": "changed@example.com",
                      "phone": "+5511999999999",
                      "phoneVerifiedAt": "2026-09-02T12:00:00Z",
                      "expiresAt": "2030-01-01T00:00:00Z",
                      "sessionPurpose": "product",
                      "hasPassword": true,
                      "hasGoogle": false,
                      "hasApple": false
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = await client.ChangeEmailAsync(
            "bgs_existing",
            "changed@example.com");

        Assert.Equal(HttpMethod.Put, captured?.Method);
        Assert.Equal("/v1/account/email", captured?.RequestUri?.AbsolutePath);
        Assert.NotNull(requestJson);
        using var requestBody = JsonDocument.Parse(requestJson);
        Assert.Equal(
            "bgs_existing",
            requestBody.RootElement.GetProperty("sessionToken").GetString());
        Assert.Equal(
            "changed@example.com",
            requestBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("changed@example.com", result.Email);
    }

    [Theory]
    [InlineData("link-google", "/v1/account/social/google/link")]
    [InlineData("unlink-google", "/v1/account/social/google/unlink")]
    [InlineData("link-apple", "/v1/account/social/apple/link")]
    [InlineData("unlink-apple", "/v1/account/social/apple/unlink")]
    public async Task SocialManagement_UsesOnlyTheSelectedProviderContract(
        string operation,
        string expectedPath)
    {
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedAuthorization = null;
        string? capturedJson = null;
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri?.AbsolutePath;
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CurrentIdentityJson(identityId, sessionId),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var result = operation switch
        {
            "link-google" => await client.LinkGoogleAsync(
                "bgs_existing",
                "google-id-token",
                "google-access-token"),
            "unlink-google" => await client.UnlinkGoogleAsync("bgs_existing"),
            "link-apple" => await client.LinkAppleAsync(
                "bgs_existing",
                "apple-identity-token"),
            "unlink-apple" => await client.UnlinkAppleAsync("bgs_existing"),
            _ => throw new InvalidOperationException("Unsupported test operation."),
        };

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal(expectedPath, capturedPath);
        Assert.Equal("Bearer bgic_test.secret", capturedAuthorization);
        Assert.NotNull(capturedJson);
        using var requestBody = JsonDocument.Parse(capturedJson);
        var propertyNames = requestBody.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedProperties = operation switch
        {
            "link-google" => new[] { "accessToken", "idToken", "sessionToken" },
            "link-apple" => ["identityToken", "sessionToken"],
            _ => ["sessionToken"],
        };
        Assert.Equal(expectedProperties, propertyNames);
        Assert.Equal(
            "bgs_existing",
            requestBody.RootElement.GetProperty("sessionToken").GetString());
        Assert.Equal(identityId, result.IdentityId);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(BullgateSessionPurpose.Product, result.Purpose);
    }

    [Fact]
    public async Task UnknownSessionPurpose_IsNeverAcceptedAsAnIssuedOrCurrentSession()
    {
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri?.AbsolutePath == "/v1/auth/google"
                    ? IssuedSessionJson(identityId, "administrator")
                    : CurrentIdentityJson(identityId, sessionId, "administrator"),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.GoogleAsync("google-id-token", "google-access-token"));
        await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.LinkGoogleAsync(
                "bgs_existing",
                "google-id-token",
                "google-access-token"));
    }

    [Fact]
    public async Task CurrentIdentityResponse_RejectsMissingSessionIdentifier()
    {
        var identityId = Guid.CreateVersion7();
        var handler = new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CurrentIdentityJson(identityId, Guid.Empty),
            });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        var exception = await Assert.ThrowsAsync<BullgateAccessUnavailableException>(
            () => client.ChangeEmailAsync("bgs_existing", "changed@example.com"));

        Assert.Contains(
            "incomplete current identity response",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAccount_UsesTheCurrentSessionToken()
    {
        HttpRequestMessage? captured = null;
        string? requestJson = null;
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            requestJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();

        await client.DeleteAccountAsync("bgs_existing");

        Assert.Equal(HttpMethod.Delete, captured?.Method);
        Assert.Equal("/v1/account", captured?.RequestUri?.AbsolutePath);
        Assert.NotNull(requestJson);
        using var requestBody = JsonDocument.Parse(requestJson);
        Assert.Equal(
            "bgs_existing",
            requestBody.RootElement.GetProperty("sessionToken").GetString());
    }

    [Fact]
    public async Task StartFlow_ForwardsManagePhoneWithTheCurrentSession()
    {
        HttpRequestMessage? captured = null;
        string? requestJson = null;
        var flowId = Guid.CreateVersion7();
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            requestJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = Json(
                    $$"""
                    {
                      "flowCapability": "bgf_opaque",
                      "snapshot": {
                        "protocolVersion": 1,
                        "flowId": "{{flowId}}",
                        "revision": 1,
                        "intent": "managePhone",
                        "status": "active",
                        "expiresAt": "2030-01-01T00:10:00Z",
                        "step": { "type": "collectPhone" },
                        "actions": []
                      }
                    }
                    """),
            };
        });
        await using var services = CreateServices(handler);
        var client = services.GetRequiredService<IBullgateAccessClient>();
        const string applicationClientKey = "android-internal";

        var result = await client.StartFlowAsync(
            Guid.CreateVersion7(),
            [1],
            "managePhone",
            applicationClientKey,
            "bgs_existing");

        Assert.Equal(HttpMethod.Post, captured?.Method);
        Assert.Equal("/v1/access/flows", captured?.RequestUri?.AbsolutePath);
        Assert.NotNull(requestJson);
        using var requestBody = JsonDocument.Parse(requestJson);
        Assert.Equal(
            "managePhone",
            requestBody.RootElement.GetProperty("intent").GetString());
        Assert.Equal(
            applicationClientKey,
            requestBody.RootElement.GetProperty("applicationClientKey").GetString());
        Assert.Equal(
            "bgs_existing",
            requestBody.RootElement.GetProperty("sessionToken").GetString());
        Assert.Equal("managePhone", result.Snapshot.Intent);
    }

    private static ServiceProvider CreateServices(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBullgateAccess(options =>
        {
            options.BaseAddress = new Uri("https://access.example.test");
            options.IntegrationCredential = "bgic_test.secret";
        });
        services.ConfigureHttpClientDefaults(
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider();
    }

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private static StringContent IssuedSessionJson(
        Guid identityId,
        string purpose = "product") =>
        Json(
            $$"""
            {
              "identityId": "{{identityId}}",
              "email": "person@example.com",
              "sessionToken": "bgs_secret",
              "sessionExpiresAt": "2030-01-01T00:00:00Z",
              "sessionPurpose": "{{purpose}}",
              "isNew": true,
              "hasPassword": true,
              "hasGoogle": false,
              "hasApple": false
            }
            """);

    private static StringContent CurrentIdentityJson(
        Guid identityId,
        Guid sessionId,
        string purpose = "product") =>
        Json(
            $$"""
            {
              "identityId": "{{identityId}}",
              "sessionId": "{{sessionId}}",
              "email": "person@example.com",
              "expiresAt": "2030-01-01T00:00:00Z",
              "sessionPurpose": "{{purpose}}",
              "hasPassword": true,
              "hasGoogle": false,
              "hasApple": false
            }
            """);

    private static StringContent ActiveFlowJson(Guid flowId) =>
        Json(
            $$"""
            {
              "flowCapability": "bgf_opaque",
              "snapshot": {
                "protocolVersion": 1,
                "flowId": "{{flowId}}",
                "revision": 1,
                "intent": "continueRegistration",
                "status": "active",
                "expiresAt": "2030-01-01T00:10:00Z",
                "step": { "type": "collectProfile" },
                "actions": []
              }
            }
            """);

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
