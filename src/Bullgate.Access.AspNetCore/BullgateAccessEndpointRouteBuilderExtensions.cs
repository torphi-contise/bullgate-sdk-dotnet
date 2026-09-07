using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bullgate.Access.AspNetCore;

/// <summary>Maps the authenticated Bullgate Access BFF into an ASP.NET Core application.</summary>
public static class BullgateAccessEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps Access routes and connects identity sessions to the consumer-owned
    /// registration and product-profile adapter.
    /// </summary>
    /// <typeparam name="TRegistration">The consumer-defined registration request payload.</typeparam>
    /// <typeparam name="TApplication">The consumer-defined public product-profile payload.</typeparam>
    /// <param name="endpoints">The consumer application's endpoint route builder.</param>
    /// <returns>The mapped route group under <see cref="BullgateAccessDefaults.RoutePrefix"/>.</returns>
    public static RouteGroupBuilder MapBullgateAccess<TRegistration, TApplication>(
        this IEndpointRouteBuilder endpoints)
        where TRegistration : class
        where TApplication : class
    {
        var group = endpoints.MapGroup(BullgateAccessDefaults.RoutePrefix)
            .WithTags("Bullgate Access");

        group.MapPost(
                "/register",
                RegisterAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/login",
                LoginAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/google",
                GoogleAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/apple",
                AppleAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapGet(
                "/session",
                SessionAsync<TRegistration, TApplication>);
        group.MapPost(
                "/logout",
                LogoutAsync)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapGet(
                "/config/application-clients/{applicationClientKey}",
                GetApplicationClientConfigurationAsync)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/password/recovery/email",
                RequestPasswordRecoveryByEmailAsync)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/password/recovery/phone",
                RequestPasswordRecoveryByPhoneAsync)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/password/recovery/phone/confirm",
                ConfirmPasswordRecoveryByPhoneAsync)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/password/recovery/reset",
                ResetPasswordAsync)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/account/password",
                ChangePasswordAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPut(
                "/account/email",
                ChangeEmailAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/account/google",
                LinkGoogleAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapDelete(
                "/account/google",
                UnlinkGoogleAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/account/apple",
                LinkAppleAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapDelete(
                "/account/apple",
                UnlinkAppleAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/flows",
                StartFlowAsync<TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapGet(
                "/flows/{flowId:guid}",
                GetFlowAsync<TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());
        group.MapPost(
                "/flows/{flowId:guid}/actions",
                ActOnFlowAsync<TRegistration, TApplication>)
            .WithMetadata(new SkipBullgateSessionAuthentication());

        return group;
    }

    private static async Task<IResult> RegisterAsync<TRegistration, TApplication>(
        RegistrationRequest<TRegistration> request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        if (request.Application is null)
        {
            return Results.BadRequest(new AccessErrorResponse(
                "missing-fields",
                "application"));
        }

        BullgateIssuedSession issued;
        try
        {
            issued = await access.RegisterAsync(
                request.Email,
                request.Password,
                cancellationToken);
        }
        catch (BullgateAccessRejectedException exception)
            when (exception.StatusCode == StatusCodes.Status409Conflict
                && exception.Error == "email-taken")
        {
            // Registration retries may arrive after Access committed the identity but
            // before the consumer committed its profile. Re-authentication resumes
            // that operation without weakening the consumer's idempotency boundary.
            try
            {
                issued = await access.LoginAsync(
                    request.Email,
                    request.Password,
                    cancellationToken);
            }
            catch (BullgateAccessRejectedException loginException)
            {
                return ToRejectedResult(loginException);
            }
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }

        var session = ToSession(issued);
        BullgateApplicationProvisionResult<TApplication> provisioned;
        try
        {
            provisioned = await application.ProvisionAsync(
                session,
                request.Application,
                cancellationToken);
        }
        catch (BullgateApplicationRejectedException exception)
        {
            // A session without its required product profile must not remain usable.
            await access.RevokeCurrentSessionAsync(
                issued.SessionToken,
                cancellationToken);
            return Results.Json(
                new AccessErrorResponse(exception.Error, exception.Field),
                statusCode: exception.StatusCode);
        }
        cookie.Write(httpContext, issued.SessionToken, issued.ExpiresAt);

        return Results.Ok(ToSessionEnvelope(session, provisioned.Application));
    }

    private static async Task<IResult> LoginAsync<TRegistration, TApplication>(
        LoginRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        BullgateIssuedSession issued;
        try
        {
            issued = await access.LoginAsync(
                request.Email,
                request.Password,
                cancellationToken);
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }

        var session = ToSession(issued);
        var resolved = await application.ResolveAsync(session, cancellationToken);
        if (resolved is null)
        {
            // Access authentication alone never provisions or authorizes a product profile.
            await access.RevokeCurrentSessionAsync(
                issued.SessionToken,
                cancellationToken);
            return Results.Conflict(
                new AccessErrorResponse("application-not-provisioned"));
        }

        cookie.Write(httpContext, issued.SessionToken, issued.ExpiresAt);
        return Results.Ok(ToSessionEnvelope(session, resolved));
    }

    private static async Task<IResult> GoogleAsync<TRegistration, TApplication>(
        SocialGoogleRequest<TRegistration> request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        try
        {
            var issued = await access.GoogleAsync(
                request.IdToken,
                request.AccessToken,
                cancellationToken);
            return await CompleteSocialSessionAsync(
                issued,
                request.Application,
                access,
                cookie,
                application,
                httpContext,
                cancellationToken);
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> AppleAsync<TRegistration, TApplication>(
        SocialAppleRequest<TRegistration> request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        try
        {
            var issued = await access.AppleAsync(
                request.IdentityToken,
                cancellationToken);
            return await CompleteSocialSessionAsync(
                issued,
                request.Application,
                access,
                cookie,
                application,
                httpContext,
                cancellationToken);
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> CompleteSocialSessionAsync<TRegistration, TApplication>(
        BullgateIssuedSession issued,
        TRegistration? registration,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        var session = ToSession(issued);
        var resolved = await application.ResolveAsync(session, cancellationToken);
        if (resolved is null)
        {
            if (registration is null)
            {
                // Do not retain a newly issued social session when product registration
                // data is absent; the caller must restart with the complete intent.
                await access.RevokeCurrentSessionAsync(
                    issued.SessionToken,
                    cancellationToken);
                return Results.BadRequest(new AccessErrorResponse(
                    "missing-fields",
                    "application"));
            }

            try
            {
                var provisioned = await application.ProvisionAsync(
                    session,
                    registration,
                    cancellationToken);
                resolved = provisioned.Application;
            }
            catch (BullgateApplicationRejectedException exception)
            {
                await access.RevokeCurrentSessionAsync(
                    issued.SessionToken,
                    cancellationToken);
                return Results.Json(
                    new AccessErrorResponse(exception.Error, exception.Field),
                    statusCode: exception.StatusCode);
            }
        }

        cookie.Write(httpContext, issued.SessionToken, issued.ExpiresAt);
        return Results.Ok(ToSessionEnvelope(session, resolved));
    }

    private static async Task<IResult> SessionAsync<TRegistration, TApplication>(
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        var session = httpContext.Features
            .Get<IBullgateSessionFeature>()
            ?.Session;
        if (session is null)
        {
            return Results.Unauthorized();
        }

        var resolved = await application.ResolveAsync(session, cancellationToken);
        if (resolved is null)
        {
            return Results.Unauthorized();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(ToSessionEnvelope(session, resolved));
    }

    private static async Task<IResult> LogoutAsync(
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateFlowCookie flowCookie,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var sessionToken = cookie.Read(httpContext);
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            await access.RevokeCurrentSessionAsync(
                sessionToken,
                cancellationToken);
        }

        cookie.Delete(httpContext);
        flowCookie.Delete(httpContext);
        return Results.NoContent();
    }

    private static async Task<IResult> GetApplicationClientConfigurationAsync(
        string applicationClientKey,
        IBullgateAccessClient access,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationClientKey))
        {
            return InvalidApplicationClientKey();
        }

        try
        {
            var configuration = await access.GetApplicationClientConfigurationAsync(
                applicationClientKey,
                cancellationToken);
            return Results.Ok(configuration);
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> RequestPasswordRecoveryByEmailAsync(
        PasswordRecoveryEmailRequest request,
        IBullgateAccessClient access,
        CancellationToken cancellationToken)
    {
        try
        {
            await access.RequestPasswordRecoveryByEmailAsync(
                request.Email,
                cancellationToken);
            return Results.Accepted();
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> RequestPasswordRecoveryByPhoneAsync(
        PhonePasswordRecoveryRequest request,
        IBullgateAccessClient access,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationClientKey))
        {
            return InvalidApplicationClientKey();
        }

        try
        {
            var challenge = await access.RequestPasswordRecoveryByPhoneAsync(
                request.Phone,
                request.ApplicationClientKey,
                cancellationToken);
            return Results.Ok(challenge);
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> ConfirmPasswordRecoveryByPhoneAsync(
        PhonePasswordRecoveryConfirmRequest request,
        IBullgateAccessClient access,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await access.ConfirmPasswordRecoveryByPhoneAsync(
                request.Phone,
                request.Code,
                cancellationToken);
            return Results.Ok(token);
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> ResetPasswordAsync(
        PasswordRecoveryResetRequest request,
        IBullgateAccessClient access,
        CancellationToken cancellationToken)
    {
        try
        {
            await access.ResetPasswordAsync(
                request.Token,
                request.NewPassword,
                cancellationToken);
            return Results.NoContent();
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static Task<IResult> ChangePasswordAsync<TRegistration, TApplication>(
        ChangePasswordRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class =>
        CompleteCurrentIdentityAsync(
            token => access.ChangePasswordAsync(
                token,
                request.CurrentPassword,
                request.NewPassword,
                cancellationToken),
            cookie,
            application,
            httpContext,
            cancellationToken);

    private static Task<IResult> ChangeEmailAsync<TRegistration, TApplication>(
        ChangeEmailRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class =>
        CompleteCurrentIdentityAsync(
            token => access.ChangeEmailAsync(
                token,
                request.Email,
                cancellationToken),
            cookie,
            application,
            httpContext,
            cancellationToken);

    private static Task<IResult> LinkGoogleAsync<TRegistration, TApplication>(
        SocialGoogleManagementRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class =>
        CompleteCurrentIdentityAsync(
            token => access.LinkGoogleAsync(
                token,
                request.IdToken,
                request.AccessToken,
                cancellationToken),
            cookie,
            application,
            httpContext,
            cancellationToken);

    private static Task<IResult> UnlinkGoogleAsync<TRegistration, TApplication>(
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class =>
        CompleteCurrentIdentityAsync(
            token => access.UnlinkGoogleAsync(token, cancellationToken),
            cookie,
            application,
            httpContext,
            cancellationToken);

    private static Task<IResult> LinkAppleAsync<TRegistration, TApplication>(
        SocialAppleManagementRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class =>
        CompleteCurrentIdentityAsync(
            token => access.LinkAppleAsync(
                token,
                request.IdentityToken,
                cancellationToken),
            cookie,
            application,
            httpContext,
            cancellationToken);

    private static Task<IResult> UnlinkAppleAsync<TRegistration, TApplication>(
        IBullgateAccessClient access,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class =>
        CompleteCurrentIdentityAsync(
            token => access.UnlinkAppleAsync(token, cancellationToken),
            cookie,
            application,
            httpContext,
            cancellationToken);

    private static async Task<IResult> CompleteCurrentIdentityAsync<
        TRegistration,
        TApplication>(
        Func<string?, Task<BullgateSession>> operation,
        IBullgateSessionCookie cookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        try
        {
            var session = await operation(cookie.Read(httpContext));
            if (session.Purpose != BullgateSessionPurpose.Product)
            {
                return Results.Conflict(
                    new AccessErrorResponse("session-purpose-invalid"));
            }

            var resolved = await application.ResolveAsync(session, cancellationToken);
            if (resolved is null)
            {
                return Results.Conflict(
                    new AccessErrorResponse("application-not-provisioned"));
            }

            httpContext.Response.Headers.CacheControl = "no-store";
            return Results.Ok(ToSessionEnvelope(session, resolved));
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> StartFlowAsync<TApplication>(
        StartFlowRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie sessionCookie,
        IBullgateFlowCookie flowCookie,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TApplication : class
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationClientKey))
        {
            return InvalidApplicationClientKey();
        }

        try
        {
            var flow = await access.StartFlowAsync(
                request.RequestId,
                request.ProtocolVersions,
                request.Intent,
                request.ApplicationClientKey,
                sessionCookie.Read(httpContext),
                cancellationToken);
            flowCookie.Write(
                httpContext,
                flow.Capability,
                flow.Snapshot.ExpiresAt);
            return Results.Ok(
                new BullgateAccessFlowEnvelope<TApplication>(flow.Snapshot));
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> GetFlowAsync<TApplication>(
        Guid flowId,
        IBullgateAccessClient access,
        IBullgateFlowCookie flowCookie,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TApplication : class
    {
        try
        {
            var flow = await access.GetFlowAsync(
                flowId,
                flowCookie.Read(httpContext),
                cancellationToken);
            flowCookie.Write(
                httpContext,
                flow.Capability,
                flow.Snapshot.ExpiresAt);
            return Results.Ok(
                new BullgateAccessFlowEnvelope<TApplication>(flow.Snapshot));
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static async Task<IResult> ActOnFlowAsync<TRegistration, TApplication>(
        Guid flowId,
        FlowActionRequest request,
        IBullgateAccessClient access,
        IBullgateSessionCookie sessionCookie,
        IBullgateFlowCookie flowCookie,
        IBullgateAccessApplication<TRegistration, TApplication> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        where TRegistration : class
        where TApplication : class
    {
        if (request.Action is null)
        {
            return Results.BadRequest(new AccessErrorResponse(
                "invalid-request",
                "action"));
        }

        try
        {
            var flow = await access.ActOnFlowAsync(
                flowId,
                flowCookie.Read(httpContext),
                request.RequestId,
                request.ExpectedRevision,
                request.Action,
                cancellationToken);
            if (flow.IssuedSession is null)
            {
                flowCookie.Write(
                    httpContext,
                    flow.Capability,
                    flow.Snapshot.ExpiresAt);
                return Results.Ok(
                    new BullgateAccessFlowEnvelope<TApplication>(flow.Snapshot));
            }

            if (flow.IssuedSession.Purpose != BullgateSessionPurpose.Product)
            {
                throw new BullgateAccessUnavailableException(
                    "Bullgate completed an access flow with a non-product session.");
            }

            // The flow result carries bearer material, but online introspection remains
            // authoritative before a consumer session and principal are established.
            var session = await access.IntrospectAsync(
                flow.IssuedSession.SessionToken,
                cancellationToken);
            if (session is null || session.Purpose != BullgateSessionPurpose.Product)
            {
                throw new BullgateAccessUnavailableException(
                    "Bullgate completed an access flow with an inactive session.");
            }

            var resolved = await application.ResolveAsync(session, cancellationToken);
            if (resolved is null)
            {
                await access.RevokeCurrentSessionAsync(
                    flow.IssuedSession.SessionToken,
                    cancellationToken);
                return Results.Conflict(
                    new AccessErrorResponse("application-not-provisioned"));
            }

            sessionCookie.Write(
                httpContext,
                flow.IssuedSession.SessionToken,
                flow.IssuedSession.ExpiresAt);
            flowCookie.Delete(httpContext);
            return Results.Ok(
                new BullgateAccessFlowEnvelope<TApplication>(
                    flow.Snapshot,
                    ToSessionEnvelope(session, resolved)));
        }
        catch (BullgateAccessRejectedException exception)
        {
            return ToRejectedResult(exception);
        }
    }

    private static BullgateSession ToSession(BullgateIssuedSession issued) =>
        new(
            issued.IdentityId,
            null,
            issued.Email,
            issued.Phone,
            issued.PhoneVerifiedAt,
            issued.ExpiresAt,
            issued.Purpose,
            issued.HasPassword,
            issued.HasGoogle,
            issued.GoogleEmail,
            issued.HasApple,
            issued.AppleEmail);

    private static BullgateAccessSessionEnvelope<TApplication> ToSessionEnvelope<TApplication>(
        BullgateSession session,
        TApplication application)
        where TApplication : class =>
        new(
            session.Purpose == BullgateSessionPurpose.Product
                ? BullgateAccessSessionState.Authenticated
                : BullgateAccessSessionState.Registration,
            session.IdentityId,
            session.Email,
            session.Phone,
            session.PhoneVerifiedAt,
            session.ExpiresAt,
            session.HasPassword,
            session.HasGoogle,
            session.GoogleEmail,
            session.HasApple,
            session.AppleEmail,
            session.Purpose == BullgateSessionPurpose.Product
                ? application
                : null);

    private static IResult ToRejectedResult(
        BullgateAccessRejectedException exception) =>
        Results.Json(
            new AccessErrorResponse(exception.Error, exception.Field),
            statusCode: exception.StatusCode);

    private static IResult InvalidApplicationClientKey() =>
        Results.BadRequest(new AccessErrorResponse(
            "application-client-invalid",
            "applicationClientKey"));

    private sealed record RegistrationRequest<TRegistration>(
        string? Email,
        string? Password,
        TRegistration? Application)
        where TRegistration : class;

    private sealed record LoginRequest(string? Email, string? Password);

    private sealed record PasswordRecoveryEmailRequest(string? Email);

    private sealed record PhonePasswordRecoveryRequest(
        string? Phone,
        string? ApplicationClientKey);

    private sealed record PhonePasswordRecoveryConfirmRequest(
        string? Phone,
        string? Code);

    private sealed record PasswordRecoveryResetRequest(
        string? Token,
        string? NewPassword);

    private sealed record ChangePasswordRequest(
        string? CurrentPassword,
        string? NewPassword);

    private sealed record ChangeEmailRequest(string? Email);

    private sealed record SocialGoogleManagementRequest(
        string? IdToken,
        string? AccessToken);

    private sealed record SocialAppleManagementRequest(string? IdentityToken);

    private sealed record SocialGoogleRequest<TRegistration>(
        string? IdToken,
        string? AccessToken,
        TRegistration? Application)
        where TRegistration : class;

    private sealed record SocialAppleRequest<TRegistration>(
        string? IdentityToken,
        TRegistration? Application)
        where TRegistration : class;

    private sealed record StartFlowRequest(
        Guid RequestId,
        IReadOnlyList<int>? ProtocolVersions,
        string? Intent,
        string? ApplicationClientKey);

    private sealed record FlowActionRequest(
        Guid RequestId,
        int ExpectedRevision,
        BullgateAccessFlowAction? Action);

    private sealed record AccessErrorResponse(string Error, string? Field = null);
}
