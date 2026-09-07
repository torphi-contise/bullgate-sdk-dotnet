using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bullgate.Access.AspNetCore;

internal sealed class BullgateAccessClient(HttpClient httpClient) : IBullgateAccessClient
{
    private const string FlowCapabilityHeader = "Bullgate-Flow-Capability";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<BullgateIssuedSession> RegisterAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default) =>
        SendForSessionAsync(
            HttpMethod.Post,
            "/v1/auth/register",
            new EmailPasswordRequest(email, password),
            cancellationToken);

    public Task<BullgateIssuedSession> LoginAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default) =>
        SendForSessionAsync(
            HttpMethod.Post,
            "/v1/auth/login",
            new EmailPasswordRequest(email, password),
            cancellationToken);

    public Task<BullgateIssuedSession> GoogleAsync(
        string? idToken,
        string? accessToken,
        CancellationToken cancellationToken = default) =>
        SendForSessionAsync(
            HttpMethod.Post,
            "/v1/auth/google",
            new GoogleAuthRequest(idToken, accessToken),
            cancellationToken);

    public Task<BullgateIssuedSession> AppleAsync(
        string? identityToken,
        CancellationToken cancellationToken = default) =>
        SendForSessionAsync(
            HttpMethod.Post,
            "/v1/auth/apple",
            new AppleAuthRequest(identityToken),
            cancellationToken);

    public async Task<BullgateSession?> IntrospectAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/auth/session/introspect",
            new SessionTokenRequest(sessionToken),
            cancellationToken);
        var body = await ReadJsonAsync<SessionIntrospectionResponse>(
            response,
            cancellationToken);

        if (body.Active is null)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete introspection response.");
        }

        if (!body.Active.Value)
        {
            return null;
        }

        if (body.IdentityId is null
            || body.SessionId is null
            || string.IsNullOrWhiteSpace(body.Email)
            || body.ExpiresAt is null)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete active session.");
        }

        return new BullgateSession(
            body.IdentityId.Value,
            body.SessionId.Value,
            body.Email,
            body.Phone,
            body.PhoneVerifiedAt,
            body.ExpiresAt.Value,
            ParsePurpose(body.SessionPurpose),
            body.HasPassword ?? false,
            body.HasGoogle ?? false,
            body.GoogleEmail,
            body.HasApple ?? false,
            body.AppleEmail);
    }

    public async Task RevokeCurrentSessionAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/auth/session/revoke",
            new SessionTokenRequest(sessionToken),
            cancellationToken);
    }

    public async Task<string> GetEmailAsync(
        Guid identityId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync<object>(
            HttpMethod.Get,
            $"/v1/identities/{identityId:D}/email",
            request: null,
            capability: null,
            cancellationToken);
        var body = await ReadJsonAsync<IdentityEmailResponse>(
            response,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(body.Email))
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete identity email response.");
        }

        return body.Email;
    }

    public async Task<BullgateIdentityPhone> GetPhoneAsync(
        Guid identityId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync<object>(
            HttpMethod.Get,
            $"/v1/identities/{identityId:D}/phone",
            request: null,
            capability: null,
            cancellationToken);
        var body = await ReadJsonAsync<IdentityPhoneResponse>(
            response,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(body.Phone))
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete identity phone response.");
        }

        return new BullgateIdentityPhone(body.Phone, body.VerifiedAt);
    }

    public async Task<BullgateApplicationClientConfiguration>
        GetApplicationClientConfigurationAsync(
            string applicationClientKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationClientKey);

        using var response = await SendAsync<object>(
            HttpMethod.Get,
            $"/v1/config/application-clients/{Uri.EscapeDataString(applicationClientKey)}",
            request: null,
            capability: null,
            cancellationToken);
        var body = await ReadJsonAsync<ApplicationClientConfigurationResponse>(
            response,
            cancellationToken);
        if (body.ConfigurationVersion <= 0
            || body.Common.ValueKind != JsonValueKind.Object
            || body.Client is null
            || string.IsNullOrWhiteSpace(body.Client.Key)
            || string.IsNullOrWhiteSpace(body.Client.Name)
            || !IsApplicationClientPlatform(body.Client.Platform)
            || body.Client.Configuration.ValueKind != JsonValueKind.Object)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete application client configuration.");
        }

        return new BullgateApplicationClientConfiguration(
            body.ConfigurationVersion,
            body.Common.Clone(),
            new BullgateApplicationClient(
                body.Client.Key,
                body.Client.Name,
                body.Client.Platform!,
                body.Client.ApplicationId,
                body.Client.SigningIdentity,
                body.Client.SmsRetrieverAppHash,
                body.Client.Configuration.Clone()));
    }

    public async Task RequestPasswordRecoveryByEmailAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/auth/password/recovery/email",
            new PasswordRecoveryEmailRequest(email),
            cancellationToken);
    }

    public async Task<BullgatePhonePasswordRecoveryChallenge>
        RequestPasswordRecoveryByPhoneAsync(
            string? phone,
            string applicationClientKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationClientKey);

        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/auth/password/recovery/phone",
            new PhonePasswordRecoveryRequest(phone, applicationClientKey),
            cancellationToken);
        var body = await ReadJsonAsync<PhonePasswordRecoveryResponse>(
            response,
            cancellationToken);
        if (body.ExpiresAt == default || body.ResendAvailableAt == default)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete phone password recovery response.");
        }

        return new BullgatePhonePasswordRecoveryChallenge(
            body.ExpiresAt,
            body.ResendAvailableAt);
    }

    public async Task<BullgatePasswordResetToken> ConfirmPasswordRecoveryByPhoneAsync(
        string? phone,
        string? code,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/auth/password/recovery/phone/confirm",
            new PhonePasswordRecoveryConfirmRequest(phone, code),
            cancellationToken);
        var body = await ReadJsonAsync<PhonePasswordRecoveryConfirmResponse>(
            response,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(body.Token) || body.ExpiresAt == default)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete phone password recovery confirmation.");
        }

        return new BullgatePasswordResetToken(body.Token, body.ExpiresAt);
    }

    public async Task ResetPasswordAsync(
        string? token,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/auth/password/recovery/reset",
            new PasswordRecoveryResetRequest(token, newPassword),
            cancellationToken);
    }

    public Task<BullgateSession> ChangePasswordAsync(
        string? sessionToken,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken = default) =>
        SendForCurrentIdentityAsync(
            HttpMethod.Post,
            "/v1/account/password",
            new ChangePasswordRequest(sessionToken, currentPassword, newPassword),
            cancellationToken);

    public Task<BullgateSession> ChangeEmailAsync(
        string? sessionToken,
        string? email,
        CancellationToken cancellationToken = default) =>
        SendForCurrentIdentityAsync(
            HttpMethod.Put,
            "/v1/account/email",
            new ChangeEmailRequest(sessionToken, email),
            cancellationToken);

    public Task<BullgateSession> LinkGoogleAsync(
        string? sessionToken,
        string? idToken,
        string? accessToken,
        CancellationToken cancellationToken = default) =>
        SendForCurrentIdentityAsync(
            HttpMethod.Post,
            "/v1/account/social/google/link",
            new GoogleManagementRequest(sessionToken, idToken, accessToken),
            cancellationToken);

    public Task<BullgateSession> UnlinkGoogleAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default) =>
        SendForCurrentIdentityAsync(
            HttpMethod.Post,
            "/v1/account/social/google/unlink",
            new SessionTokenRequest(sessionToken),
            cancellationToken);

    public Task<BullgateSession> LinkAppleAsync(
        string? sessionToken,
        string? identityToken,
        CancellationToken cancellationToken = default) =>
        SendForCurrentIdentityAsync(
            HttpMethod.Post,
            "/v1/account/social/apple/link",
            new AppleManagementRequest(sessionToken, identityToken),
            cancellationToken);

    public Task<BullgateSession> UnlinkAppleAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default) =>
        SendForCurrentIdentityAsync(
            HttpMethod.Post,
            "/v1/account/social/apple/unlink",
            new SessionTokenRequest(sessionToken),
            cancellationToken);

    public async Task DeleteAccountAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            "/v1/account",
            new SessionTokenRequest(sessionToken),
            cancellationToken);
    }

    public async Task<BullgateAccessFlow> StartFlowAsync(
        Guid requestId,
        IReadOnlyList<int>? protocolVersions,
        string? intent,
        string applicationClientKey,
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationClientKey);

        using var response = await SendAsync(
            HttpMethod.Post,
            "/v1/access/flows",
            new StartAccessFlowRequest(
                requestId,
                protocolVersions,
                intent,
                applicationClientKey,
                sessionToken),
            cancellationToken);
        return await ReadFlowAsync(response, cancellationToken);
    }

    public async Task<BullgateAccessFlow> GetFlowAsync(
        Guid flowId,
        string? capability,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync<object>(
            HttpMethod.Get,
            $"/v1/access/flows/{flowId:D}",
            request: null,
            capability,
            cancellationToken);
        return await ReadFlowAsync(response, cancellationToken);
    }

    public async Task<BullgateAccessFlow> ActOnFlowAsync(
        Guid flowId,
        string? capability,
        Guid requestId,
        int expectedRevision,
        BullgateAccessFlowAction action,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v1/access/flows/{flowId:D}/actions",
            new AccessFlowActionRequest(requestId, expectedRevision, action),
            capability,
            cancellationToken);
        return await ReadFlowAsync(response, cancellationToken);
    }

    private async Task<BullgateIssuedSession> SendForSessionAsync<TRequest>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, request, cancellationToken);
        var body = await ReadJsonAsync<EmailPasswordResponse>(response, cancellationToken);
        if (body.IdentityId == Guid.Empty
            || string.IsNullOrWhiteSpace(body.Email)
            || string.IsNullOrWhiteSpace(body.SessionToken)
            || body.SessionExpiresAt == default
            || body.IsNew is null
            || body.HasPassword is null
            || body.HasGoogle is null
            || body.HasApple is null)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete session response.");
        }

        return new BullgateIssuedSession(
            body.IdentityId,
            body.Email,
            body.Phone,
            body.PhoneVerifiedAt,
            body.SessionToken,
            body.SessionExpiresAt,
            ParsePurpose(body.SessionPurpose),
            body.IsNew.Value,
            body.HasPassword.Value,
            body.HasGoogle.Value,
            body.GoogleEmail,
            body.HasApple.Value,
            body.AppleEmail);
    }

    private async Task<BullgateSession> SendForCurrentIdentityAsync<TRequest>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            method,
            path,
            request,
            cancellationToken);
        var body = await ReadJsonAsync<CurrentIdentityResponse>(
            response,
            cancellationToken);
        if (body.IdentityId == Guid.Empty
            || body.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(body.Email)
            || body.ExpiresAt == default
            || body.HasPassword is null
            || body.HasGoogle is null
            || body.HasApple is null)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete current identity response.");
        }

        return new BullgateSession(
            body.IdentityId,
            body.SessionId,
            body.Email,
            body.Phone,
            body.PhoneVerifiedAt,
            body.ExpiresAt,
            ParsePurpose(body.SessionPurpose),
            body.HasPassword.Value,
            body.HasGoogle.Value,
            body.GoogleEmail,
            body.HasApple.Value,
            body.AppleEmail);
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken) =>
        await SendAsync(
            method,
            path,
            request,
            capability: null,
            cancellationToken);

    private async Task<HttpResponseMessage> SendAsync<TRequest>(
        HttpMethod method,
        string path,
        TRequest? request,
        string? capability,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(method, path);
            if (request is not null)
            {
                message.Content = JsonContent.Create(request, options: JsonOptions);
            }
            if (!string.IsNullOrWhiteSpace(capability))
            {
                message.Headers.TryAddWithoutValidation(
                    FlowCapabilityHeader,
                    capability);
            }

            var response = await httpClient.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var error = await TryReadErrorAsync(response, cancellationToken);
            if (error is not null
                && !string.IsNullOrWhiteSpace(error.Error)
                && IsDomainRejection(response.StatusCode, error.Error))
            {
                response.Dispose();
                throw new BullgateAccessRejectedException(
                    (int)response.StatusCode,
                    error.Error,
                    error.Field);
            }

            // An unrecognized error response is operationally unusable. Treating it as
            // a business rejection could make the host act on an invented contract.
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new BullgateAccessUnavailableException(
                $"Bullgate Access returned operational status {statusCode}.");
        }
        catch (BullgateAccessRejectedException)
        {
            throw;
        }
        catch (BullgateAccessUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access could not be reached.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access returned invalid JSON.",
                exception);
        }
    }

    private static bool IsDomainRejection(
        HttpStatusCode statusCode,
        string error) =>
        // 503 is normally availability failure. The delivery-specific code is a
        // deliberate domain rejection because retrying the same user input is useful.
        statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.NotFound
            or HttpStatusCode.Conflict
            or HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.ServiceUnavailable
            && string.Equals(
                error,
                "verification-delivery-unavailable",
                StringComparison.Ordinal);

    private static bool IsApplicationClientPlatform(string? platform) =>
        string.Equals(platform, "android", StringComparison.Ordinal)
        || string.Equals(platform, "ios", StringComparison.Ordinal)
        || string.Equals(platform, "web", StringComparison.Ordinal);

    private static async Task<BullgateAccessFlow> ReadFlowAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await ReadJsonAsync<AccessFlowResponse>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(body.FlowCapability)
            || body.Snapshot is null
            || body.Snapshot.ProtocolVersion <= 0
            || body.Snapshot.FlowId == Guid.Empty
            || body.Snapshot.Revision <= 0
            || string.IsNullOrWhiteSpace(body.Snapshot.Intent)
            || string.IsNullOrWhiteSpace(body.Snapshot.Status)
            || body.Snapshot.ExpiresAt == default
            || body.Snapshot.Actions is null)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate returned an incomplete access flow response.");
        }

        BullgateAccessFlowIssuedSession? issued = null;
        if (body.IssuedSession is not null)
        {
            if (body.IssuedSession.IdentityId == Guid.Empty
                || body.IssuedSession.SessionId == Guid.Empty
                || string.IsNullOrWhiteSpace(body.IssuedSession.SessionToken)
                || body.IssuedSession.ExpiresAt == default)
            {
                throw new BullgateAccessUnavailableException(
                    "Bullgate returned an incomplete access flow session.");
            }

            issued = new BullgateAccessFlowIssuedSession(
                body.IssuedSession.IdentityId,
                body.IssuedSession.SessionId,
                body.IssuedSession.SessionToken,
                body.IssuedSession.ExpiresAt,
                ParsePurpose(body.IssuedSession.Purpose));
        }

        return new BullgateAccessFlow(
            body.FlowCapability,
            body.Snapshot,
            issued);
    }

    private static BullgateSessionPurpose ParsePurpose(string? purpose) => purpose switch
    {
        "product" => BullgateSessionPurpose.Product,
        "registration" => BullgateSessionPurpose.Registration,
        _ => throw new BullgateAccessUnavailableException(
            "Bullgate returned an unsupported session purpose."),
    };

    private static async Task<TResponse> ReadJsonAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(
                JsonOptions,
                cancellationToken)
                ?? throw new BullgateAccessUnavailableException(
                    "Bullgate Access returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access returned invalid JSON.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access returned an unsupported response format.",
                exception);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access timed out while reading the response.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BullgateAccessUnavailableException(
                "Bullgate Access response could not be read.",
                exception);
        }
    }

    private static async Task<AccessErrorResponse?> TryReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AccessErrorResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record EmailPasswordRequest(string? Email, string? Password);

    private sealed record GoogleAuthRequest(string? IdToken, string? AccessToken);

    private sealed record AppleAuthRequest(string? IdentityToken);

    private sealed record SessionTokenRequest(string? SessionToken);

    private sealed record IdentityEmailResponse(string? Email);

    private sealed record IdentityPhoneResponse(
        string? Phone,
        DateTimeOffset? VerifiedAt);

    private sealed record ApplicationClientConfigurationResponse(
        int ConfigurationVersion,
        JsonElement Common,
        ApplicationClientResponse? Client);

    private sealed record ApplicationClientResponse(
        string? Key,
        string? Name,
        string? Platform,
        string? ApplicationId,
        string? SigningIdentity,
        string? SmsRetrieverAppHash,
        JsonElement Configuration);

    private sealed record PasswordRecoveryEmailRequest(string? Email);

    private sealed record PhonePasswordRecoveryRequest(
        string? Phone,
        string ApplicationClientKey);

    private sealed record PhonePasswordRecoveryResponse(
        DateTimeOffset ExpiresAt,
        DateTimeOffset ResendAvailableAt);

    private sealed record PhonePasswordRecoveryConfirmRequest(
        string? Phone,
        string? Code);

    private sealed record PhonePasswordRecoveryConfirmResponse(
        string? Token,
        DateTimeOffset ExpiresAt);

    private sealed record PasswordRecoveryResetRequest(
        string? Token,
        string? NewPassword);

    private sealed record ChangePasswordRequest(
        string? SessionToken,
        string? CurrentPassword,
        string? NewPassword);

    private sealed record ChangeEmailRequest(
        string? SessionToken,
        string? Email);

    private sealed record GoogleManagementRequest(
        string? SessionToken,
        string? IdToken,
        string? AccessToken);

    private sealed record AppleManagementRequest(
        string? SessionToken,
        string? IdentityToken);

    private sealed record EmailPasswordResponse(
        Guid IdentityId,
        string Email,
        string? Phone,
        DateTimeOffset? PhoneVerifiedAt,
        string SessionToken,
        DateTimeOffset SessionExpiresAt,
        string? SessionPurpose,
        bool? IsNew,
        bool? HasPassword,
        bool? HasGoogle,
        string? GoogleEmail,
        bool? HasApple,
        string? AppleEmail);

    private sealed record SessionIntrospectionResponse(
        bool? Active,
        Guid? IdentityId,
        Guid? SessionId,
        string? Email,
        string? Phone,
        DateTimeOffset? PhoneVerifiedAt,
        DateTimeOffset? ExpiresAt,
        string? SessionPurpose,
        bool? HasPassword,
        bool? HasGoogle,
        string? GoogleEmail,
        bool? HasApple,
        string? AppleEmail);

    private sealed record CurrentIdentityResponse(
        Guid IdentityId,
        Guid SessionId,
        string Email,
        string? Phone,
        DateTimeOffset? PhoneVerifiedAt,
        DateTimeOffset ExpiresAt,
        string? SessionPurpose,
        bool? HasPassword,
        bool? HasGoogle,
        string? GoogleEmail,
        bool? HasApple,
        string? AppleEmail);

    private sealed record StartAccessFlowRequest(
        Guid RequestId,
        IReadOnlyList<int>? ProtocolVersions,
        string? Intent,
        string ApplicationClientKey,
        string? SessionToken);

    private sealed record AccessFlowActionRequest(
        Guid RequestId,
        int ExpectedRevision,
        BullgateAccessFlowAction Action);

    private sealed record AccessFlowResponse(
        string? FlowCapability,
        BullgateAccessFlowSnapshot? Snapshot,
        AccessFlowIssuedSessionResponse? IssuedSession);

    private sealed record AccessFlowIssuedSessionResponse(
        Guid IdentityId,
        Guid SessionId,
        string? SessionToken,
        DateTimeOffset ExpiresAt,
        string? Purpose);

    private sealed record AccessErrorResponse(string Error, string? Field);
}
