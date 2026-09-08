using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bullgate.Access.AspNetCore;

internal sealed class BullgateSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IBullgateAccessClient access,
    IBullgateSessionCookie cookie,
    IBullgatePrincipalResolver principals)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<SkipBullgateSessionAuthentication>() is not null)
        {
            return AuthenticateResult.NoResult();
        }

        var token = cookie.Read(Context);
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await access.IntrospectAsync(token, Context.RequestAborted);
        if (session is null)
        {
            cookie.Delete(Context);
            return AuthenticateResult.NoResult();
        }

        Context.Features.Set<IBullgateSessionFeature>(
            new BullgateSessionFeature(session));
        if (session.Purpose != BullgateSessionPurpose.Product)
        {
            return AuthenticateResult.NoResult();
        }

        var local = await principals.ResolveAsync(session, Context.RequestAborted);
        if (local is null || string.IsNullOrWhiteSpace(local.Subject))
        {
            cookie.Delete(Context);
            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, local.Subject),
            new(ClaimTypes.Email, session.Email),
            new(BullgateAccessDefaults.IdentityIdClaim, session.IdentityId.ToString("D")),
        };
        if (session.SessionId is not null)
        {
            claims.Add(new Claim(
                BullgateAccessDefaults.SessionIdClaim,
                session.SessionId.Value.ToString("D")));
        }
        if (!string.IsNullOrWhiteSpace(local.Name))
        {
            claims.Add(new Claim(ClaimTypes.Name, local.Name));
        }
        if (local.AdditionalClaims is not null)
        {
            claims.AddRange(local.AdditionalClaims);
        }

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, BullgateAccessDefaults.AuthenticationScheme));
        return AuthenticateResult.Success(
            new AuthenticationTicket(
                principal,
                BullgateAccessDefaults.AuthenticationScheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

/// <summary>Endpoint metadata that prevents the Bullgate session handler from authenticating a route.</summary>
/// <remarks>
/// This marker suppresses only the Bullgate session authentication handler. It
/// does not grant anonymous access, satisfy authorization requirements, or
/// suppress another authentication scheme. Apply it only when the endpoint
/// handles the relevant Access bearer or cookie boundary explicitly.
/// </remarks>
public sealed class SkipBullgateSessionAuthentication;
