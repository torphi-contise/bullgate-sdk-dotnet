namespace Bullgate.Access.AspNetCore;

/// <summary>Provides typed server-to-server operations for Bullgate Access.</summary>
/// <remarks>
/// All methods use the configured integration credential. Bearer session tokens,
/// reset tokens, provider tokens, and flow capabilities must remain trusted data.
/// Structured Access rejections raise <see cref="BullgateAccessRejectedException"/>.
/// Transport failures, timeouts, and unusable responses raise
/// <see cref="BullgateAccessUnavailableException"/>. Caller cancellation is preserved.
/// </remarks>
public interface IBullgateAccessClient
{
    /// <summary>Registers an email/password identity and issues a registration or product session.</summary>
    /// <param name="email">The identity email supplied by the end user.</param>
    /// <param name="password">The plaintext password supplied over the trusted server boundary.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The newly issued bearer session and identity facts.</returns>
    Task<BullgateIssuedSession> RegisterAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>Authenticates an email/password identity and issues a session.</summary>
    /// <param name="email">The identity email supplied by the end user.</param>
    /// <param name="password">The plaintext password supplied over the trusted server boundary.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The newly issued bearer session and identity facts.</returns>
    Task<BullgateIssuedSession> LoginAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>Authenticates or registers an identity using trusted Google provider tokens.</summary>
    /// <param name="idToken">The Google ID token, when available.</param>
    /// <param name="accessToken">The Google access token, when available.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The newly issued bearer session and identity facts.</returns>
    Task<BullgateIssuedSession> GoogleAsync(
        string? idToken,
        string? accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>Authenticates or registers an identity using an Apple identity token.</summary>
    /// <param name="identityToken">The Apple identity token supplied by the native provider flow.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The newly issued bearer session and identity facts.</returns>
    Task<BullgateIssuedSession> AppleAsync(
        string? identityToken,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the active session for a bearer token, or null when it is inactive.</summary>
    /// <param name="sessionToken">The opaque Bullgate session token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The active session, or null when the token is not active.</returns>
    Task<BullgateSession?> IntrospectAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes the session represented by the supplied bearer token.</summary>
    /// <param name="sessionToken">The opaque Bullgate session token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes after Access accepts the revocation.</returns>
    Task RevokeCurrentSessionAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the Access-owned email for an identity in the credential's scope.</summary>
    /// <param name="identityId">The stable Bullgate identity identifier.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The current normalized identity email.</returns>
    Task<string> GetEmailAsync(
        Guid identityId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the Access-owned phone and verification state for an identity.</summary>
    /// <param name="identityId">The stable Bullgate identity identifier.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The current phone number and its optional verification timestamp.</returns>
    Task<BullgateIdentityPhone> GetPhoneAsync(
        Guid identityId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads public configuration for a stable application-client key.</summary>
    /// <param name="applicationClientKey">The public, stable client key within the credential's environment.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Public environment and application-client configuration.</returns>
    Task<BullgateApplicationClientConfiguration>
        GetApplicationClientConfigurationAsync(
            string applicationClientKey,
            CancellationToken cancellationToken = default);

    /// <summary>Requests password recovery by email without revealing account existence.</summary>
    /// <param name="email">The email to which recovery may be delivered.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes after the request is accepted.</returns>
    Task RequestPasswordRecoveryByEmailAsync(
        string? email,
        CancellationToken cancellationToken = default);

    /// <summary>Requests a phone recovery code for the specified public application client.</summary>
    /// <param name="phone">The phone number to prove.</param>
    /// <param name="applicationClientKey">The public client key used for delivery configuration.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The challenge expiration and resend window.</returns>
    Task<BullgatePhonePasswordRecoveryChallenge> RequestPasswordRecoveryByPhoneAsync(
        string? phone,
        string applicationClientKey,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms a phone recovery code and returns a short-lived password reset token.</summary>
    /// <param name="phone">The phone number associated with the challenge.</param>
    /// <param name="code">The one-time code supplied by the end user.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A short-lived bearer token that authorizes password reset.</returns>
    Task<BullgatePasswordResetToken> ConfirmPasswordRecoveryByPhoneAsync(
        string? phone,
        string? code,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a new password using a valid recovery token.</summary>
    /// <param name="token">The short-lived password reset bearer token.</param>
    /// <param name="newPassword">The new plaintext password over the trusted server boundary.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes after the password is changed.</returns>
    Task ResetPasswordAsync(
        string? token,
        string? newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the current identity's password and returns the refreshed session.</summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="currentPassword">The current plaintext password.</param>
    /// <param name="newPassword">The replacement plaintext password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The refreshed session state.</returns>
    Task<BullgateSession> ChangePasswordAsync(
        string? sessionToken,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the current identity's email and returns the refreshed session.</summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="email">The replacement email address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The refreshed session state.</returns>
    Task<BullgateSession> ChangeEmailAsync(
        string? sessionToken,
        string? email,
        CancellationToken cancellationToken = default);

    /// <summary>Links a Google identity to the current session.</summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="idToken">The Google ID token, when available.</param>
    /// <param name="accessToken">The Google access token, when available.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The refreshed session state.</returns>
    Task<BullgateSession> LinkGoogleAsync(
        string? sessionToken,
        string? idToken,
        string? accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>Unlinks Google from the current identity when another access method remains.</summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The refreshed session state.</returns>
    Task<BullgateSession> UnlinkGoogleAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>Links an Apple identity to the current session.</summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="identityToken">The Apple identity token supplied by the native provider flow.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The refreshed session state.</returns>
    Task<BullgateSession> LinkAppleAsync(
        string? sessionToken,
        string? identityToken,
        CancellationToken cancellationToken = default);

    /// <summary>Unlinks Apple from the current identity when another access method remains.</summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The refreshed session state.</returns>
    Task<BullgateSession> UnlinkAppleAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the current Access identity. Cross-database order and
    /// retry behavior remain the consumer application's responsibility.
    /// </summary>
    /// <param name="sessionToken">The current opaque session token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes after Access deletes the identity.</returns>
    Task DeleteAccountAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>Starts an AccessFlow using an idempotent request ID and supported protocol versions.</summary>
    /// <param name="requestId">A non-empty idempotency identifier for this start intent.</param>
    /// <param name="protocolVersions">The protocol versions accepted by the caller.</param>
    /// <param name="intent">The requested flow intent.</param>
    /// <param name="applicationClientKey">The public client key used for flow configuration.</param>
    /// <param name="sessionToken">The current session token when the intent requires authentication.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The selected protocol snapshot and temporary bearer capability.</returns>
    Task<BullgateAccessFlow> StartFlowAsync(
        Guid requestId,
        IReadOnlyList<int>? protocolVersions,
        string? intent,
        string applicationClientKey,
        string? sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a flow using its temporary bearer capability.</summary>
    /// <param name="flowId">The flow identifier returned at creation.</param>
    /// <param name="capability">The temporary bearer capability for that flow.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The current flow snapshot and any session issued by completion.</returns>
    Task<BullgateAccessFlow> GetFlowAsync(
        Guid flowId,
        string? capability,
        CancellationToken cancellationToken = default);

    /// <summary>Applies an idempotent action to an expected flow revision.</summary>
    /// <param name="flowId">The flow identifier returned at creation.</param>
    /// <param name="capability">The temporary bearer capability for that flow.</param>
    /// <param name="requestId">A non-empty idempotency identifier for this action intent.</param>
    /// <param name="expectedRevision">The exact flow revision on which the action was based.</param>
    /// <param name="action">The protocol action and optional input.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The next flow snapshot and any session issued by completion.</returns>
    Task<BullgateAccessFlow> ActOnFlowAsync(
        Guid flowId,
        string? capability,
        Guid requestId,
        int expectedRevision,
        BullgateAccessFlowAction action,
        CancellationToken cancellationToken = default);
}
