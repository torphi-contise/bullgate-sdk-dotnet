using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bullgate.Access.AspNetCore.Tests;

public sealed class BullgateAccessHostTests
{
    private static readonly int[] ProtocolVersion1 = [1];
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Register_ResumesAnExistingIdentityAndIssuesOnlyTheHostCookie()
    {
        var fake = new FakeAccessClient
        {
            RegisterFailure = new BullgateAccessRejectedException(
                StatusCodes.Status409Conflict,
                "email-taken",
                "email"),
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/register",
            new
            {
                email = "person@example.com",
                password = "password-123",
                application = new { name = "Person" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.RegisterCalls);
        Assert.Equal(1, fake.LoginCalls);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("bullgate.session=bgs_issued", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadFromJsonAsync<TestEnvelope>();
        Assert.NotNull(body);
        Assert.Equal(fake.IdentityId, body.IdentityId);
        Assert.Equal(BullgateAccessSessionState.Authenticated, body.State);
        Assert.Equal("+5511999999999", body.Phone);
        Assert.Equal(fake.PhoneVerifiedAt, body.PhoneVerifiedAt);
        Assert.Equal("Person", body.Application?.Name);
    }

    [Fact]
    public async Task Register_RejectsMissingApplicationBeforeIssuingSession()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/register",
            new
            {
                email = "person@example.com",
                password = "password-123",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fake.RegisterCalls);
        Assert.Equal(0, fake.LoginCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("missing-fields", error?.Error);
        Assert.Equal("application", error?.Field);
    }

    [Fact]
    public async Task Register_PreservesAccessRejectionWithoutIssuingSession()
    {
        var fake = new FakeAccessClient
        {
            RegisterFailure = new BullgateAccessRejectedException(
                StatusCodes.Status400BadRequest,
                "invalid-email",
                "email"),
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/register",
            new
            {
                email = "invalid",
                password = "password-123",
                application = new { name = "Person" },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, fake.RegisterCalls);
        Assert.Equal(0, fake.LoginCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("invalid-email", error?.Error);
        Assert.Equal("email", error?.Field);
    }

    [Fact]
    public async Task Register_ResumePreservesLoginRejectionWithoutIssuingSession()
    {
        var fake = new FakeAccessClient
        {
            RegisterFailure = new BullgateAccessRejectedException(
                StatusCodes.Status409Conflict,
                "email-taken",
                "email"),
            LoginFailure = new BullgateAccessRejectedException(
                StatusCodes.Status401Unauthorized,
                "invalid-credentials"),
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/register",
            new
            {
                email = "person@example.com",
                password = "wrong-password",
                application = new { name = "Person" },
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, fake.RegisterCalls);
        Assert.Equal(1, fake.LoginCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("invalid-credentials", error?.Error);
        Assert.Null(error?.Field);
    }

    [Fact]
    public async Task GoogleLoginWithoutProfileOrRegistration_RevokesIssuedSession()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/google",
            new { idToken = "google-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, fake.GoogleCalls);
        Assert.Equal("bgs_issued", fake.RevokedToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgs_issued", responseJson, StringComparison.Ordinal);
        var error = JsonSerializer.Deserialize<TestError>(
            responseJson,
            WebJsonOptions);
        Assert.Equal("missing-fields", error?.Error);
        Assert.Equal("application", error?.Field);
    }

    [Fact]
    public async Task GoogleProvisioningRejection_RevokesIssuedSession()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false,
            rejectProvision: true);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/google",
            new
            {
                idToken = "google-token",
                application = new { name = "Rejected Person" },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, fake.GoogleCalls);
        Assert.Equal("bgs_issued", fake.RevokedToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgs_issued", responseJson, StringComparison.Ordinal);
        var error = JsonSerializer.Deserialize<TestError>(
            responseJson,
            WebJsonOptions);
        Assert.Equal("application-rejected", error?.Error);
    }

    [Fact]
    public async Task Session_IntrospectsTheCookieAndResolvesTheLocalPrincipal()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.GetAsync("/bullgate/access/v1/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.IntrospectionCalls);
        var body = await response.Content.ReadFromJsonAsync<TestEnvelope>();
        Assert.NotNull(body);
        Assert.Equal(fake.IdentityId, body.IdentityId);
        Assert.Equal(BullgateAccessSessionState.Authenticated, body.State);
        Assert.Equal("+5511999999999", body.Phone);
        Assert.Equal(fake.PhoneVerifiedAt, body.PhoneVerifiedAt);
        Assert.Equal("Existing", body.Application?.Name);
    }

    [Fact]
    public async Task SessionWithoutCookie_ReturnsUnauthorizedWithoutCallingAccess()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/bullgate/access/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.IntrospectionCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SessionWithoutApplication_ReturnsUnauthorizedWithoutInventingProfile()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.GetAsync("/bullgate/access/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, fake.IntrospectionCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task MissingSessionCookie_ChallengesWithoutCallingAccess()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.IntrospectionCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UnknownSession_DeletesCookieAndChallenges()
    {
        var fake = new FakeAccessClient { IntrospectionMissing = true };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_unknown");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, fake.IntrospectionCalls);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("bullgate.session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task InvalidLocalPrincipal_DeletesCookieAndChallenges(
        string? principalSubject)
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            principalSubject: principalSubject);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, fake.IntrospectionCalls);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("bullgate.session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidSessionWithoutRequiredClaim_ReturnsForbidden()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.GetAsync("/forbidden");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, fake.IntrospectionCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ApplicationClientConfiguration_ProxiesOnlyThePublicDocument()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = await client.GetAsync(
            "/bullgate/access/v1/config/application-clients/android-internal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("android-internal", fake.ConfigurationApplicationClientKey);
        Assert.Equal(0, fake.IntrospectionCalls);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(
            7,
            body.RootElement.GetProperty("configurationVersion").GetInt32());
        Assert.Equal(
            "https://example.test/support",
            body.RootElement
                .GetProperty("common")
                .GetProperty("supportUrl")
                .GetString());
        Assert.Equal(
            "android",
            body.RootElement
                .GetProperty("client")
                .GetProperty("platform")
                .GetString());
        Assert.Equal(
            "A6U92ovJLGE",
            body.RootElement
                .GetProperty("client")
                .GetProperty("smsRetrieverAppHash")
                .GetString());
    }

    [Fact]
    public async Task RegistrationSession_IsVisibleToSessionButCannotAuthorizeProductRoutes()
    {
        var fake = new FakeAccessClient
        {
            IntrospectionPurpose = BullgateSessionPurpose.Registration,
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_registration");

        var sessionResponse = await client.GetAsync("/bullgate/access/v1/session");
        var protectedResponse = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<TestEnvelope>();
        Assert.Equal(BullgateAccessSessionState.Registration, session?.State);
        Assert.Null(session?.Application);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task FlowCapability_StaysInTheHostOnlyCookie()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_registration");

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/flows",
            new
            {
                requestId = Guid.CreateVersion7(),
                protocolVersions = ProtocolVersion1,
                intent = "continueRegistration",
                applicationClientKey = "android-internal",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bgs_registration", fake.FlowSessionToken);
        Assert.Equal("android-internal", fake.FlowApplicationClientKey);
        var flowCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("bullgate.flow=", StringComparison.Ordinal));
        Assert.Contains("httponly", flowCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", flowCookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgf_opaque", body, StringComparison.Ordinal);
        Assert.DoesNotContain("bgs_registration", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFlow_UsesHostOnlyCapabilityCookieWithoutExposingIt()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.flow=bgf_existing");
        var flowId = Guid.CreateVersion7();

        var response = await client.GetAsync(
            $"/bullgate/access/v1/flows/{flowId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(flowId, fake.GetFlowId);
        Assert.Equal("bgf_existing", fake.GetFlowCapability);
        var flowCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("bullgate.flow=", StringComparison.Ordinal));
        Assert.Contains("bullgate.flow=bgf_opaque", flowCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", flowCookie, StringComparison.OrdinalIgnoreCase);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgf_existing", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("bgf_opaque", responseJson, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(responseJson);
        Assert.Equal(
            flowId,
            body.RootElement.GetProperty("flow").GetProperty("flowId").GetGuid());
    }

    [Fact]
    public async Task FlowActionWithoutAction_IsRejectedBeforeCallingAccess()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.flow=bgf_existing");
        var flowId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/bullgate/access/v1/flows/{flowId:D}/actions",
            new
            {
                requestId = Guid.CreateVersion7(),
                expectedRevision = 1,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fake.ActOnFlowCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("invalid-request", error?.Error);
        Assert.Equal("action", error?.Field);
    }

    [Fact]
    public async Task NonTerminalFlowAction_RotatesOnlyTheCapabilityCookie()
    {
        var fake = new FakeAccessClient { CompleteFlowActions = false };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.flow=bgf_existing");
        var flowId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/bullgate/access/v1/flows/{flowId:D}/actions",
            new
            {
                requestId = Guid.CreateVersion7(),
                expectedRevision = 1,
                action = new
                {
                    id = Guid.CreateVersion7(),
                    type = "submitProfile",
                    input = new { name = "Person" },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.ActOnFlowCalls);
        Assert.Equal("bgf_existing", fake.ActOnFlowCapability);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(
            cookies,
            value => value.StartsWith("bullgate.flow=bgf_opaque", StringComparison.Ordinal));
        Assert.DoesNotContain(
            cookies,
            value => value.StartsWith("bullgate.session=", StringComparison.Ordinal));
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgf_existing", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("bgf_opaque", responseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TerminalFlowAction_ReplacesTheRestrictedSessionWithoutExposingTokens()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            "bullgate.session=bgs_registration; bullgate.flow=bgf_opaque");
        var flowId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/bullgate/access/v1/flows/{flowId:D}/actions",
            new
            {
                requestId = Guid.CreateVersion7(),
                expectedRevision = 1,
                action = new
                {
                    id = Guid.CreateVersion7(),
                    type = "skipRegistration",
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(
            cookies,
            value => value.StartsWith(
                "bullgate.session=bgs_product",
                StringComparison.Ordinal));
        Assert.Contains(
            cookies,
            value => value.StartsWith("bullgate.flow=", StringComparison.Ordinal)
                && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgs_product", body, StringComparison.Ordinal);
        Assert.DoesNotContain("bgf_opaque", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            BullgateAccessSessionState.Authenticated,
            json.RootElement.GetProperty("session").GetProperty("state").GetString());
    }

    [Fact]
    public async Task CompletedFlowWithoutApplication_RevokesIssuedSession()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.flow=bgf_existing");
        var flowId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/bullgate/access/v1/flows/{flowId:D}/actions",
            new
            {
                requestId = Guid.CreateVersion7(),
                expectedRevision = 1,
                action = new
                {
                    id = Guid.CreateVersion7(),
                    type = "skipRegistration",
                },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("bgs_product", fake.RevokedToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("application-not-provisioned", error?.Error);
    }

    [Fact]
    public async Task CompletedFlowWithInactiveSession_ReturnsUnavailableWithoutCookie()
    {
        var fake = new FakeAccessClient { IntrospectionMissing = true };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.flow=bgf_existing");
        var flowId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/bullgate/access/v1/flows/{flowId:D}/actions",
            new
            {
                requestId = Guid.CreateVersion7(),
                expectedRevision = 1,
                action = new
                {
                    id = Guid.CreateVersion7(),
                    type = "skipRegistration",
                },
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, fake.IntrospectionCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("access-unavailable", error?.Error);
    }

    [Fact]
    public async Task CompletedFlowWithNonProductSession_ReturnsUnavailableWithoutCookie()
    {
        var fake = new FakeAccessClient
        {
            CompletedFlowPurpose = BullgateSessionPurpose.Registration,
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.flow=bgf_existing");
        var flowId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/bullgate/access/v1/flows/{flowId:D}/actions",
            new
            {
                requestId = Guid.CreateVersion7(),
                expectedRevision = 1,
                action = new
                {
                    id = Guid.CreateVersion7(),
                    type = "skipRegistration",
                },
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, fake.IntrospectionCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("access-unavailable", error?.Error);
    }

    [Fact]
    public async Task Login_RevokesTheIssuedSessionWhenTheHostProfileDoesNotExist()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/login",
            new
            {
                email = "person@example.com",
                password = "password-123",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("bgs_issued", fake.RevokedToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Register_RevokesTheIssuedSessionWhenTheHostRejectsProvisioning()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            rejectProvision: true);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/register",
            new
            {
                email = "person@example.com",
                password = "password-123",
                application = new { name = "Person" },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bgs_issued", fake.RevokedToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Google_ProvisionsMissingHostProfileWhenApplicationPayloadIsPresent()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/google",
            new
            {
                idToken = "google-token",
                application = new { name = "Google Person" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.GoogleCalls);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("bullgate.session=bgs_issued", setCookie, StringComparison.Ordinal);
        var body = await response.Content.ReadFromJsonAsync<TestEnvelope>();
        Assert.Equal("Google Person", body?.Application?.Name);
    }

    [Fact]
    public async Task Apple_ProvisionsMissingHostProfileWithoutExposingIdentityToken()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: false);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/apple",
            new
            {
                identityToken = "apple-identity-token",
                application = new { name = "Apple Person" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.AppleCalls);
        Assert.Equal("apple-identity-token", fake.AppleAuthenticationToken);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("bullgate.session=bgs_issued", setCookie, StringComparison.Ordinal);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("apple-identity-token", responseJson, StringComparison.Ordinal);
        var body = JsonSerializer.Deserialize<TestEnvelope>(
            responseJson,
            WebJsonOptions);
        Assert.Equal("Apple Person", body?.Application?.Name);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("apple")]
    public async Task SocialAuthentication_PreservesAccessRejectionWithoutCookie(
        string provider)
    {
        var rejection = new BullgateAccessRejectedException(
            StatusCodes.Status401Unauthorized,
            "provider-token-invalid",
            "credential");
        var fake = new FakeAccessClient
        {
            GoogleFailure = provider == "google" ? rejection : null,
            AppleFailure = provider == "apple" ? rejection : null,
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var response = provider == "google"
            ? await client.PostAsJsonAsync(
                "/bullgate/access/v1/google",
                new { idToken = "google-token" })
            : await client.PostAsJsonAsync(
                "/bullgate/access/v1/apple",
                new { identityToken = "apple-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("google-token", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("apple-token", responseJson, StringComparison.Ordinal);
        var error = JsonSerializer.Deserialize<TestError>(
            responseJson,
            WebJsonOptions);
        Assert.Equal("provider-token-invalid", error?.Error);
        Assert.Equal("credential", error?.Field);
    }

    [Fact]
    public async Task Logout_RevokesBeforeDeletingTheCookie()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/logout",
            new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("bgs_existing", fake.RevokedToken);
        Assert.Equal(0, fake.IntrospectionCalls);
        var setCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("bullgate.session=", StringComparison.Ordinal));
        Assert.Contains("bullgate.session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordRecovery_ProxiesTheFinalAnonymousContract()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        const string applicationClientKey = "android-internal";

        var emailResponse = await client.PostAsJsonAsync(
            "/bullgate/access/v1/password/recovery/email",
            new { email = "person@example.com" });
        var phoneResponse = await client.PostAsJsonAsync(
            "/bullgate/access/v1/password/recovery/phone",
            new { phone = "+5511999999999", applicationClientKey });
        var confirmResponse = await client.PostAsJsonAsync(
            "/bullgate/access/v1/password/recovery/phone/confirm",
            new { phone = "+5511999999999", code = "123456" });
        var resetResponse = await client.PostAsJsonAsync(
            "/bullgate/access/v1/password/recovery/reset",
            new { token = "reset-token", newPassword = "new-password-456" });

        Assert.Equal(HttpStatusCode.Accepted, emailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, phoneResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
        Assert.Equal("person@example.com", fake.RecoveryEmail);
        Assert.Equal("+5511999999999", fake.RecoveryPhone);
        Assert.Equal(applicationClientKey, fake.RecoveryApplicationClientKey);
        Assert.Equal("123456", fake.RecoveryCode);
        Assert.Equal("reset-token", fake.ResetToken);
        Assert.Equal("new-password-456", fake.ResetPassword);

        var challenge = await phoneResponse.Content
            .ReadFromJsonAsync<BullgatePhonePasswordRecoveryChallenge>();
        Assert.Equal(fake.RecoveryExpiresAt, challenge?.ExpiresAt);
        var confirmation = await confirmResponse.Content
            .ReadFromJsonAsync<BullgatePasswordResetToken>();
        Assert.Equal("reset-token", confirmation?.Token);
        Assert.Equal(fake.RecoveryExpiresAt, confirmation?.ExpiresAt);
    }

    [Fact]
    public async Task ApplicationClientOperations_RejectBlankKeysAtTheBffBoundary()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();

        var phoneResponse = await client.PostAsJsonAsync(
            "/bullgate/access/v1/password/recovery/phone",
            new { phone = "+5511999999999", applicationClientKey = " " });
        var flowResponse = await client.PostAsJsonAsync(
            "/bullgate/access/v1/flows",
            new
            {
                requestId = Guid.CreateVersion7(),
                protocolVersions = ProtocolVersion1,
                intent = "continueRegistration",
                applicationClientKey = "\t",
            });

        Assert.Equal(HttpStatusCode.BadRequest, phoneResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, flowResponse.StatusCode);
        var phoneError = await phoneResponse.Content.ReadFromJsonAsync<TestError>();
        var flowError = await flowResponse.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("application-client-invalid", phoneError?.Error);
        Assert.Equal("applicationClientKey", phoneError?.Field);
        Assert.Equal("application-client-invalid", flowError?.Error);
        Assert.Equal("applicationClientKey", flowError?.Field);
        Assert.Null(fake.RecoveryApplicationClientKey);
        Assert.Null(fake.FlowApplicationClientKey);
    }

    [Fact]
    public async Task CurrentIdentityOperations_UseTheHostOnlySessionAndReturnACompleteEnvelope()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/account/password",
            new
            {
                currentPassword = "password-123",
                newPassword = "new-password-456",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bgs_existing", fake.CurrentIdentityToken);
        Assert.Equal("change-password", fake.CurrentIdentityOperation);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.True(root.GetProperty("hasPassword").GetBoolean());
        Assert.True(root.GetProperty("hasGoogle").GetBoolean());
        Assert.Equal(
            "+5511999999999",
            root.GetProperty("phone").GetString());
        Assert.Equal(
            fake.PhoneVerifiedAt,
            root.GetProperty("phoneVerifiedAt").GetDateTimeOffset());
        Assert.Equal("google@example.test", root.GetProperty("googleEmail").GetString());
        Assert.Equal("Existing", root.GetProperty("application").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("sessionToken", out _));
    }

    [Fact]
    public async Task ChangeEmail_ReturnsTheUpdatedSessionWithoutVerification()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.PutAsJsonAsync(
            "/bullgate/access/v1/account/email",
            new { email = "changed@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bgs_existing", fake.CurrentIdentityToken);
        Assert.Equal("change-email", fake.CurrentIdentityOperation);
        Assert.Equal("changed@example.com", fake.ChangedEmail);
        var body = await response.Content.ReadFromJsonAsync<TestEnvelope>();
        Assert.Equal("changed@example.com", body?.Email);
    }

    [Fact]
    public async Task AccountDeletion_IsOwnedByTheConsumingApplication()
    {
        var deletionOrder = new List<string>();
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(
            fake,
            deletionOrder: deletionOrder);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            "bullgate.session=bgs_existing; bullgate.flow=bgf_existing");

        var response = await client.DeleteAsync("/bullgate/access/v1/account");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(deletionOrder);
        Assert.Null(fake.DeletedAccountToken);
    }

    [Fact]
    public async Task SocialManagement_ForwardsProviderCredentialsWithoutReturningThem()
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/account/google",
            new { idToken = "google-secret", accessToken = "access-secret" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bgs_existing", fake.CurrentIdentityToken);
        Assert.Equal("google-secret", fake.GoogleIdToken);
        Assert.Equal("access-secret", fake.GoogleAccessToken);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("google-secret", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("access-secret", responseJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unlink-google")]
    [InlineData("link-apple")]
    [InlineData("unlink-apple")]
    public async Task RemainingSocialManagementRoutes_UseHostSessionWithoutLeakingCredentials(
        string operation)
    {
        var fake = new FakeAccessClient();
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = operation switch
        {
            "unlink-google" => await client.DeleteAsync(
                "/bullgate/access/v1/account/google"),
            "link-apple" => await client.PostAsJsonAsync(
                "/bullgate/access/v1/account/apple",
                new { identityToken = "apple-management-token" }),
            "unlink-apple" => await client.DeleteAsync(
                "/bullgate/access/v1/account/apple"),
            _ => throw new InvalidOperationException("Unsupported test operation."),
        };

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bgs_existing", fake.CurrentIdentityToken);
        Assert.Equal(operation, fake.CurrentIdentityOperation);
        if (operation == "link-apple")
        {
            Assert.Equal("apple-management-token", fake.AppleManagementToken);
        }
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bgs_existing", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("apple-management-token", responseJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("registration", "session-purpose-invalid")]
    [InlineData("missing-application", "application-not-provisioned")]
    public async Task CurrentIdentityOperation_RejectsUnusableConsumerSession(
        string fault,
        string expectedError)
    {
        var fake = new FakeAccessClient
        {
            CurrentIdentityPurpose = fault == "registration"
                ? BullgateSessionPurpose.Registration
                : BullgateSessionPurpose.Product,
        };
        await using var host = await CreateHostAsync(
            fake,
            resolveApplication: fault != "missing-application");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.PutAsJsonAsync(
            "/bullgate/access/v1/account/email",
            new { email = "changed@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("bgs_existing", fake.CurrentIdentityToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal(expectedError, error?.Error);
    }

    [Fact]
    public async Task CurrentIdentityOperation_PreservesAccessRejectionWithoutLeakingPassword()
    {
        var fake = new FakeAccessClient
        {
            CurrentIdentityFailure = new BullgateAccessRejectedException(
                StatusCodes.Status400BadRequest,
                "password-invalid",
                "currentPassword"),
        };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.PostAsJsonAsync(
            "/bullgate/access/v1/account/password",
            new
            {
                currentPassword = "current-secret",
                newPassword = "new-secret",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bgs_existing", fake.CurrentIdentityToken);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("current-secret", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("new-secret", responseJson, StringComparison.Ordinal);
        var error = JsonSerializer.Deserialize<TestError>(
            responseJson,
            WebJsonOptions);
        Assert.Equal("password-invalid", error?.Error);
        Assert.Equal("currentPassword", error?.Field);
    }

    [Fact]
    public async Task BullgateFailure_ReturnsServiceUnavailableWithoutDeletingTheCookie()
    {
        var fake = new FakeAccessClient { IntrospectionUnavailable = true };
        await using var host = await CreateHostAsync(fake);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", "bullgate.session=bgs_existing");

        var response = await client.GetAsync("/bullgate/access/v1/session");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<TestError>();
        Assert.Equal("access-unavailable", error?.Error);
    }

    private static async Task<WebApplication> CreateHostAsync(
        FakeAccessClient access,
        bool resolveApplication = true,
        bool rejectProvision = false,
        List<string>? deletionOrder = null,
        string? principalSubject = "local-user-id")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddBullgateAccess(options =>
        {
            options.BaseAddress = new Uri("https://access.example.test");
            options.IntegrationCredential = "bgic_test.secret";
            options.CookieSecurePolicy = CookieSecurePolicy.None;
        });
        builder.Services.AddSingleton<IBullgateAccessClient>(access);
        access.DeletionOrder = deletionOrder;
        builder.Services.AddSingleton(new TestApplication(
            resolveApplication,
            rejectProvision,
            deletionOrder,
            principalSubject));
        builder.Services.AddSingleton<IBullgatePrincipalResolver>(
            services => services.GetRequiredService<TestApplication>());
        builder.Services.AddSingleton<
            IBullgateAccessApplication<TestRegistration, TestApplicationResponse>>(
            services => services.GetRequiredService<TestApplication>());
        builder.Services
            .AddAuthentication(BullgateAccessDefaults.AuthenticationScheme)
            .AddBullgateSession();

        var app = builder.Build();
        app.UseRouting();
        app.UseBullgateAccessFailures();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBullgateAccess<TestRegistration, TestApplicationResponse>();
        app.MapGet("/protected", () => Results.NoContent())
            .RequireAuthorization();
        app.MapGet("/forbidden", () => Results.NoContent())
            .RequireAuthorization(policy => policy.RequireClaim("required-claim"));
        await app.StartAsync();
        return app;
    }

    private sealed record TestRegistration(string Name);

    private sealed record TestApplicationResponse(string Name);

    private sealed record TestEnvelope(
        string State,
        Guid IdentityId,
        string Email,
        string? Phone,
        DateTimeOffset? PhoneVerifiedAt,
        DateTimeOffset SessionExpiresAt,
        TestApplicationResponse? Application);

    private sealed record TestError(string Error, string? Field = null);

    private sealed class TestApplication(
        bool resolveApplication,
        bool rejectProvision,
        List<string>? deletionOrder,
        string? principalSubject) :
        IBullgatePrincipalResolver,
        IBullgateAccessApplication<TestRegistration, TestApplicationResponse>
    {
        public Guid? DeletedIdentityId { get; private set; }

        public ValueTask<BullgateLocalPrincipal?> ResolveAsync(
            BullgateSession session,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<BullgateLocalPrincipal?>(
                principalSubject is null
                    ? null
                    : new BullgateLocalPrincipal(
                        principalSubject,
                        "Existing",
                        [new Claim("application", "test")]));

        public ValueTask<BullgateApplicationProvisionResult<TestApplicationResponse>> ProvisionAsync(
            BullgateSession session,
            TestRegistration registration,
            CancellationToken cancellationToken)
        {
            if (rejectProvision)
            {
                throw new BullgateApplicationRejectedException(
                    StatusCodes.Status400BadRequest,
                    "application-rejected");
            }

            return ValueTask.FromResult(
                new BullgateApplicationProvisionResult<TestApplicationResponse>(
                    new TestApplicationResponse(registration.Name)));
        }

        ValueTask<TestApplicationResponse?> IBullgateAccessApplication<
            TestRegistration,
            TestApplicationResponse>.ResolveAsync(
            BullgateSession session,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<TestApplicationResponse?>(
                resolveApplication
                    ? new TestApplicationResponse("Existing")
                    : null);

        public ValueTask DeleteAsync(
            BullgateSession session,
            CancellationToken cancellationToken)
        {
            DeletedIdentityId = session.IdentityId;
            deletionOrder?.Add("application");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAccessClient : IBullgateAccessClient
    {
        public Guid IdentityId { get; } = Guid.CreateVersion7();

        public BullgateAccessRejectedException? RegisterFailure { get; init; }

        public BullgateAccessRejectedException? LoginFailure { get; init; }

        public BullgateAccessRejectedException? GoogleFailure { get; init; }

        public BullgateAccessRejectedException? AppleFailure { get; init; }

        public BullgateAccessRejectedException? CurrentIdentityFailure { get; init; }

        public bool IntrospectionUnavailable { get; init; }

        public bool IntrospectionMissing { get; init; }

        public bool DeleteAccountUnavailable { get; init; }

        public BullgateSessionPurpose IssuedPurpose { get; init; } =
            BullgateSessionPurpose.Product;

        public BullgateSessionPurpose IntrospectionPurpose { get; init; } =
            BullgateSessionPurpose.Product;

        public BullgateSessionPurpose CurrentIdentityPurpose { get; init; } =
            BullgateSessionPurpose.Product;

        public int RegisterCalls { get; private set; }

        public int LoginCalls { get; private set; }

        public int GoogleCalls { get; private set; }

        public int AppleCalls { get; private set; }

        public string? AppleAuthenticationToken { get; private set; }

        public int IntrospectionCalls { get; private set; }

        public string? RevokedToken { get; private set; }

        public string? FlowSessionToken { get; private set; }

        public string? FlowApplicationClientKey { get; private set; }

        public Guid? GetFlowId { get; private set; }

        public string? GetFlowCapability { get; private set; }

        public int ActOnFlowCalls { get; private set; }

        public string? ActOnFlowCapability { get; private set; }

        public bool CompleteFlowActions { get; init; } = true;

        public BullgateSessionPurpose CompletedFlowPurpose { get; init; } =
            BullgateSessionPurpose.Product;

        public string? ConfigurationApplicationClientKey { get; private set; }

        public DateTimeOffset RecoveryExpiresAt { get; } =
            DateTimeOffset.UtcNow.AddMinutes(10);

        public DateTimeOffset PhoneVerifiedAt { get; } =
            DateTimeOffset.Parse("2026-09-02T12:00:00Z");

        public string? RecoveryEmail { get; private set; }

        public string? RecoveryPhone { get; private set; }

        public string? RecoveryApplicationClientKey { get; private set; }

        public string? RecoveryCode { get; private set; }

        public string? ResetToken { get; private set; }

        public string? ResetPassword { get; private set; }

        public string? CurrentIdentityToken { get; private set; }

        public string? CurrentIdentityOperation { get; private set; }

        public string? ChangedEmail { get; private set; }

        public string? DeletedAccountToken { get; private set; }

        public List<string>? DeletionOrder { get; set; }

        public string? GoogleIdToken { get; private set; }

        public string? GoogleAccessToken { get; private set; }

        public string? AppleManagementToken { get; private set; }

        public Task<BullgateIssuedSession> RegisterAsync(
            string? email,
            string? password,
            CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return RegisterFailure is null
                ? Task.FromResult(Issue(email, isNew: true))
                : Task.FromException<BullgateIssuedSession>(RegisterFailure);
        }

        public Task<BullgateIssuedSession> LoginAsync(
            string? email,
            string? password,
            CancellationToken cancellationToken = default)
        {
            LoginCalls++;
            return LoginFailure is null
                ? Task.FromResult(Issue(email, isNew: false))
                : Task.FromException<BullgateIssuedSession>(LoginFailure);
        }

        public Task<BullgateIssuedSession> GoogleAsync(
            string? idToken,
            string? accessToken,
            CancellationToken cancellationToken = default)
        {
            GoogleCalls++;
            return GoogleFailure is null
                ? Task.FromResult(Issue("person@example.com", isNew: true))
                : Task.FromException<BullgateIssuedSession>(GoogleFailure);
        }

        public Task<BullgateIssuedSession> AppleAsync(
            string? identityToken,
            CancellationToken cancellationToken = default)
        {
            AppleCalls++;
            AppleAuthenticationToken = identityToken;
            return AppleFailure is null
                ? Task.FromResult(Issue("person@example.com", isNew: true))
                : Task.FromException<BullgateIssuedSession>(AppleFailure);
        }

        public Task<BullgateSession?> IntrospectAsync(
            string? sessionToken,
            CancellationToken cancellationToken = default)
        {
            IntrospectionCalls++;
            if (IntrospectionUnavailable)
            {
                return Task.FromException<BullgateSession?>(
                    new BullgateAccessUnavailableException("Unavailable in test."));
            }

            if (IntrospectionMissing)
            {
                return Task.FromResult<BullgateSession?>(null);
            }

            return Task.FromResult<BullgateSession?>(
                new BullgateSession(
                    IdentityId,
                    Guid.CreateVersion7(),
                    "person@example.com",
                    "+5511999999999",
                    PhoneVerifiedAt,
                    DateTimeOffset.UtcNow.AddDays(30),
                    IntrospectionPurpose,
                    true,
                    false,
                    null,
                    false,
                    null));
        }

        public Task RevokeCurrentSessionAsync(
            string? sessionToken,
            CancellationToken cancellationToken = default)
        {
            RevokedToken = sessionToken;
            return Task.CompletedTask;
        }

        public Task<string> GetEmailAsync(
            Guid identityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("person@example.com");

        public Task<BullgateIdentityPhone> GetPhoneAsync(
            Guid identityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new BullgateIdentityPhone(
                    "+5511999999999",
                    PhoneVerifiedAt));

        public Task<BullgateApplicationClientConfiguration>
            GetApplicationClientConfigurationAsync(
                string applicationClientKey,
                CancellationToken cancellationToken = default)
        {
            ConfigurationApplicationClientKey = applicationClientKey;
            return Task.FromResult(
                new BullgateApplicationClientConfiguration(
                    7,
                    JsonSerializer.SerializeToElement(new
                    {
                        supportUrl = "https://example.test/support",
                    }),
                    new BullgateApplicationClient(
                        "android-internal",
                        "Android internal",
                        "android",
                        "app.baybo.dev",
                        "sha256:certificate",
                        "A6U92ovJLGE",
                        JsonSerializer.SerializeToElement(new
                        {
                            googleClientId = "google-client-id",
                        }))));
        }

        public Task RequestPasswordRecoveryByEmailAsync(
            string? email,
            CancellationToken cancellationToken = default)
        {
            RecoveryEmail = email;
            return Task.CompletedTask;
        }

        public Task<BullgatePhonePasswordRecoveryChallenge>
            RequestPasswordRecoveryByPhoneAsync(
                string? phone,
                string applicationClientKey,
                CancellationToken cancellationToken = default)
        {
            RecoveryPhone = phone;
            RecoveryApplicationClientKey = applicationClientKey;
            return Task.FromResult(
                new BullgatePhonePasswordRecoveryChallenge(
                    RecoveryExpiresAt,
                    RecoveryExpiresAt.AddMinutes(-9)));
        }

        public Task<BullgatePasswordResetToken> ConfirmPasswordRecoveryByPhoneAsync(
            string? phone,
            string? code,
            CancellationToken cancellationToken = default)
        {
            RecoveryPhone = phone;
            RecoveryCode = code;
            return Task.FromResult(
                new BullgatePasswordResetToken(
                    "reset-token",
                    RecoveryExpiresAt));
        }

        public Task ResetPasswordAsync(
            string? token,
            string? newPassword,
            CancellationToken cancellationToken = default)
        {
            ResetToken = token;
            ResetPassword = newPassword;
            return Task.CompletedTask;
        }

        public Task<BullgateSession> ChangePasswordAsync(
            string? sessionToken,
            string? currentPassword,
            string? newPassword,
            CancellationToken cancellationToken = default)
        {
            CurrentIdentityToken = sessionToken;
            CurrentIdentityOperation = "change-password";
            return CurrentIdentityFailure is null
                ? Task.FromResult(CurrentIdentitySession())
                : Task.FromException<BullgateSession>(CurrentIdentityFailure);
        }

        public Task<BullgateSession> ChangeEmailAsync(
            string? sessionToken,
            string? email,
            CancellationToken cancellationToken = default)
        {
            CurrentIdentityToken = sessionToken;
            CurrentIdentityOperation = "change-email";
            ChangedEmail = email;
            return Task.FromResult(
                CurrentIdentitySession(email: email ?? "person@example.com"));
        }

        public Task<BullgateSession> LinkGoogleAsync(
            string? sessionToken,
            string? idToken,
            string? accessToken,
            CancellationToken cancellationToken = default)
        {
            CurrentIdentityToken = sessionToken;
            CurrentIdentityOperation = "link-google";
            GoogleIdToken = idToken;
            GoogleAccessToken = accessToken;
            return Task.FromResult(CurrentIdentitySession());
        }

        public Task<BullgateSession> UnlinkGoogleAsync(
            string? sessionToken,
            CancellationToken cancellationToken = default)
        {
            CurrentIdentityToken = sessionToken;
            CurrentIdentityOperation = "unlink-google";
            return Task.FromResult(CurrentIdentitySession(hasGoogle: false));
        }

        public Task<BullgateSession> LinkAppleAsync(
            string? sessionToken,
            string? identityToken,
            CancellationToken cancellationToken = default)
        {
            CurrentIdentityToken = sessionToken;
            CurrentIdentityOperation = "link-apple";
            AppleManagementToken = identityToken;
            return Task.FromResult(CurrentIdentitySession(hasApple: true));
        }

        public Task<BullgateSession> UnlinkAppleAsync(
            string? sessionToken,
            CancellationToken cancellationToken = default)
        {
            CurrentIdentityToken = sessionToken;
            CurrentIdentityOperation = "unlink-apple";
            return Task.FromResult(CurrentIdentitySession());
        }

        public Task DeleteAccountAsync(
            string? sessionToken,
            CancellationToken cancellationToken = default)
        {
            DeletedAccountToken = sessionToken;
            DeletionOrder?.Add("access");
            return DeleteAccountUnavailable
                ? Task.FromException(
                    new BullgateAccessUnavailableException("Unavailable in test."))
                : Task.CompletedTask;
        }

        public Task<BullgateAccessFlow> StartFlowAsync(
            Guid requestId,
            IReadOnlyList<int>? protocolVersions,
            string? intent,
            string applicationClientKey,
            string? sessionToken,
            CancellationToken cancellationToken = default)
        {
            FlowSessionToken = sessionToken;
            FlowApplicationClientKey = applicationClientKey;
            return Task.FromResult(ActiveFlow());
        }

        public Task<BullgateAccessFlow> GetFlowAsync(
            Guid flowId,
            string? capability,
            CancellationToken cancellationToken = default)
        {
            GetFlowId = flowId;
            GetFlowCapability = capability;
            return Task.FromResult(ActiveFlow(flowId));
        }

        public Task<BullgateAccessFlow> ActOnFlowAsync(
            Guid flowId,
            string? capability,
            Guid requestId,
            int expectedRevision,
            BullgateAccessFlowAction action,
            CancellationToken cancellationToken = default)
        {
            ActOnFlowCalls++;
            ActOnFlowCapability = capability;
            return Task.FromResult(
                CompleteFlowActions
                    ? CompletedFlow(flowId)
                    : ActiveFlow(flowId));
        }

        private BullgateIssuedSession Issue(string? email, bool isNew) =>
            new(
                IdentityId,
                email ?? "person@example.com",
                "+5511999999999",
                PhoneVerifiedAt,
                "bgs_issued",
                DateTimeOffset.UtcNow.AddDays(30),
                IssuedPurpose,
                isNew,
                true,
                false,
                null,
                false,
                null);

        private BullgateSession CurrentIdentitySession(
            bool hasGoogle = true,
            bool hasApple = false,
            string email = "person@example.com") =>
            new(
                IdentityId,
                Guid.CreateVersion7(),
                email,
                "+5511999999999",
                PhoneVerifiedAt,
                DateTimeOffset.UtcNow.AddDays(30),
                CurrentIdentityPurpose,
                true,
                hasGoogle,
                hasGoogle ? "google@example.test" : null,
                hasApple,
                hasApple ? "apple@example.test" : null);

        private static BullgateAccessFlow ActiveFlow(Guid? flowId = null)
        {
            var id = flowId ?? Guid.CreateVersion7();
            return new BullgateAccessFlow(
                "bgf_opaque",
                new BullgateAccessFlowSnapshot(
                    1,
                    id,
                    1,
                    "continueRegistration",
                    "active",
                    DateTimeOffset.UtcNow.AddMinutes(30),
                    JsonSerializer.SerializeToElement(new { type = "registration" }),
                    [JsonSerializer.SerializeToElement(new
                    {
                        id = Guid.CreateVersion7(),
                        type = "skipRegistration",
                    })],
                    null,
                    null),
                null);
        }

        private BullgateAccessFlow CompletedFlow(Guid flowId) =>
            new(
                "bgf_opaque",
                new BullgateAccessFlowSnapshot(
                    1,
                    flowId,
                    2,
                    "continueRegistration",
                    "completed",
                    DateTimeOffset.UtcNow.AddMinutes(30),
                    null,
                    [],
                    null,
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "registrationCompleted",
                    })),
                new BullgateAccessFlowIssuedSession(
                    IdentityId,
                    Guid.CreateVersion7(),
                    "bgs_product",
                    DateTimeOffset.UtcNow.AddDays(30),
                    CompletedFlowPurpose));
    }
}
