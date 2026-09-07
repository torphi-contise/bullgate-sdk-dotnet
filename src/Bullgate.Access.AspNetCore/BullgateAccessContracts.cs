using System.Security.Claims;
using System.Text.Json;

namespace Bullgate.Access.AspNetCore;

/// <summary>Identifies whether an Access session may enter the product or must finish registration.</summary>
public enum BullgateSessionPurpose
{
    /// <summary>The identity may be resolved to a local product principal.</summary>
    Product,

    /// <summary>The identity is valid but still requires consumer registration.</summary>
    Registration,
}

/// <summary>
/// Describes a newly issued Access session. <see cref="SessionToken"/> is bearer
/// material and must remain on the trusted server or in an HttpOnly cookie.
/// </summary>
/// <param name="IdentityId">The stable Bullgate identity identifier.</param>
/// <param name="Email">The current normalized identity email.</param>
/// <param name="Phone">The current identity phone, when present.</param>
/// <param name="PhoneVerifiedAt">When the current phone was proven, or null when unverified.</param>
/// <param name="SessionToken">The opaque bearer token for the issued session.</param>
/// <param name="ExpiresAt">The absolute session expiration.</param>
/// <param name="Purpose">Whether the session may enter the product or must finish registration.</param>
/// <param name="IsNew">Whether Access created the identity during this operation.</param>
/// <param name="HasPassword">Whether the identity has password authentication.</param>
/// <param name="HasGoogle">Whether the identity has a linked Google login.</param>
/// <param name="GoogleEmail">The provider email reported for the linked Google login.</param>
/// <param name="HasApple">Whether the identity has a linked Apple login.</param>
/// <param name="AppleEmail">The provider email reported for the linked Apple login.</param>
public sealed record BullgateIssuedSession(
    Guid IdentityId,
    string Email,
    string? Phone,
    DateTimeOffset? PhoneVerifiedAt,
    string SessionToken,
    DateTimeOffset ExpiresAt,
    BullgateSessionPurpose Purpose,
    bool IsNew,
    bool HasPassword,
    bool HasGoogle,
    string? GoogleEmail,
    bool HasApple,
    string? AppleEmail);

/// <summary>Describes an existing session returned by online Access introspection.</summary>
/// <param name="IdentityId">The stable Bullgate identity identifier.</param>
/// <param name="SessionId">The stable session identifier, when returned by the operation.</param>
/// <param name="Email">The current normalized identity email.</param>
/// <param name="Phone">The current identity phone, when present.</param>
/// <param name="PhoneVerifiedAt">When the current phone was proven, or null when unverified.</param>
/// <param name="ExpiresAt">The absolute session expiration.</param>
/// <param name="Purpose">Whether the session may enter the product or must finish registration.</param>
/// <param name="HasPassword">Whether the identity has password authentication.</param>
/// <param name="HasGoogle">Whether the identity has a linked Google login.</param>
/// <param name="GoogleEmail">The provider email reported for the linked Google login.</param>
/// <param name="HasApple">Whether the identity has a linked Apple login.</param>
/// <param name="AppleEmail">The provider email reported for the linked Apple login.</param>
public sealed record BullgateSession(
    Guid IdentityId,
    Guid? SessionId,
    string Email,
    string? Phone,
    DateTimeOffset? PhoneVerifiedAt,
    DateTimeOffset ExpiresAt,
    BullgateSessionPurpose Purpose,
    bool HasPassword,
    bool HasGoogle,
    string? GoogleEmail,
    bool HasApple,
    string? AppleEmail);

/// <summary>Describes the expiration and resend window for a phone recovery code.</summary>
/// <param name="ExpiresAt">The absolute challenge expiration.</param>
/// <param name="ResendAvailableAt">The earliest time at which another code may be requested.</param>
public sealed record BullgatePhonePasswordRecoveryChallenge(
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt);

/// <summary>
/// Contains the short-lived bearer token issued after successful phone recovery
/// confirmation and used to reset the password.
/// </summary>
/// <param name="Token">The short-lived password reset bearer token.</param>
/// <param name="ExpiresAt">The absolute token expiration.</param>
public sealed record BullgatePasswordResetToken(
    string Token,
    DateTimeOffset ExpiresAt);

/// <summary>Contains a phone number owned by Access and its proof timestamp, if verified.</summary>
/// <param name="Phone">The current normalized phone number.</param>
/// <param name="VerifiedAt">When the current phone was proven, or null when unverified.</param>
public sealed record BullgateIdentityPhone(
    string Phone,
    DateTimeOffset? VerifiedAt);

/// <summary>Contains public configuration shared by an environment and one application client.</summary>
/// <param name="ConfigurationVersion">The public document schema version.</param>
/// <param name="Common">Extensible public values shared by all clients in the environment.</param>
/// <param name="Client">The selected public application-client definition.</param>
public sealed record BullgateApplicationClientConfiguration(
    int ConfigurationVersion,
    JsonElement Common,
    BullgateApplicationClient Client);

/// <summary>
/// Describes a stable public application client. Optional signing and SMS fields
/// are absent when they do not apply to the platform.
/// </summary>
/// <param name="Key">The stable, human-readable application-client key.</param>
/// <param name="Name">The display name used by administrators.</param>
/// <param name="Platform">The normalized <c>android</c>, <c>ios</c>, or <c>web</c> platform.</param>
/// <param name="ApplicationId">The platform application identifier, when applicable.</param>
/// <param name="SigningIdentity">The public signing identity, when applicable.</param>
/// <param name="SmsRetrieverAppHash">The Android SMS Retriever application hash, when applicable.</param>
/// <param name="Configuration">Extensible public configuration for this client.</param>
public sealed record BullgateApplicationClient(
    string Key,
    string Name,
    string Platform,
    string? ApplicationId,
    string? SigningIdentity,
    string? SmsRetrieverAppHash,
    JsonElement Configuration);

/// <summary>Contains the client-visible state of an AccessFlow revision.</summary>
/// <param name="ProtocolVersion">The protocol version selected by Access.</param>
/// <param name="FlowId">The stable identifier for this flow.</param>
/// <param name="Revision">The optimistic-concurrency revision for the next action.</param>
/// <param name="Intent">The semantic operation being completed.</param>
/// <param name="Status">The current protocol status.</param>
/// <param name="ExpiresAt">The absolute flow expiration.</param>
/// <param name="Step">The current semantic step, when one is active.</param>
/// <param name="Actions">The actions currently accepted by the protocol.</param>
/// <param name="Feedback">Structured feedback from the previous action, when present.</param>
/// <param name="Result">The terminal flow result, when present.</param>
public sealed record BullgateAccessFlowSnapshot(
    int ProtocolVersion,
    Guid FlowId,
    int Revision,
    string Intent,
    string Status,
    DateTimeOffset ExpiresAt,
    JsonElement? Step,
    IReadOnlyList<JsonElement> Actions,
    JsonElement? Feedback,
    JsonElement? Result);

/// <summary>
/// Contains bearer session material issued by a completed flow. The adapter
/// stores the token in the session cookie instead of returning it to JavaScript.
/// </summary>
/// <param name="IdentityId">The stable Bullgate identity identifier.</param>
/// <param name="SessionId">The stable identifier of the issued session.</param>
/// <param name="SessionToken">The opaque bearer token for the issued session.</param>
/// <param name="ExpiresAt">The absolute session expiration.</param>
/// <param name="Purpose">The purpose assigned to the issued session.</param>
public sealed record BullgateAccessFlowIssuedSession(
    Guid IdentityId,
    Guid SessionId,
    string SessionToken,
    DateTimeOffset ExpiresAt,
    BullgateSessionPurpose Purpose);

/// <summary>Combines a flow capability, its snapshot, and an optional issued session.</summary>
/// <param name="Capability">The temporary bearer capability required by subsequent flow calls.</param>
/// <param name="Snapshot">The current client-visible flow state.</param>
/// <param name="IssuedSession">The session issued by a terminal action, when present.</param>
public sealed record BullgateAccessFlow(
    string Capability,
    BullgateAccessFlowSnapshot Snapshot,
    BullgateAccessFlowIssuedSession? IssuedSession);

/// <summary>Represents one idempotent action submitted against an expected flow revision.</summary>
/// <param name="Id">The stable action identifier advertised by the current snapshot.</param>
/// <param name="Type">The semantic action type.</param>
/// <param name="Input">The action-specific input, when required.</param>
public sealed record BullgateAccessFlowAction(
    Guid Id,
    string Type,
    JsonElement? Input = null);

/// <summary>Defines the trusted local identity used to create an ASP.NET Core principal.</summary>
/// <param name="Subject">The stable consumer-owned subject identifier.</param>
/// <param name="Name">The optional display name.</param>
/// <param name="AdditionalClaims">Additional claims derived from trusted consumer state.</param>
public sealed record BullgateLocalPrincipal(
    string Subject,
    string? Name = null,
    IReadOnlyCollection<Claim>? AdditionalClaims = null);

/// <summary>Resolves a product-purpose Bullgate session to a consumer-owned local principal.</summary>
public interface IBullgatePrincipalResolver
{
    /// <summary>
    /// Resolves trusted local identity data for <paramref name="session"/>.
    /// Return <see langword="null"/> to reject local authentication.
    /// </summary>
    /// <param name="session">The online-introspected product-purpose session.</param>
    /// <param name="cancellationToken">Cancels local resolution.</param>
    /// <returns>The trusted local principal, or null to reject authentication.</returns>
    ValueTask<BullgateLocalPrincipal?> ResolveAsync(
        BullgateSession session,
        CancellationToken cancellationToken);
}

/// <summary>Contains the product profile committed by the consumer application.</summary>
/// <typeparam name="TApplication">The consumer-defined public product-profile type.</typeparam>
/// <param name="Application">The committed product profile.</param>
public sealed record BullgateApplicationProvisionResult<TApplication>(
    TApplication Application)
    where TApplication : class;

/// <summary>
/// Defines the consumer-owned boundary for provisioning and resolving a product
/// profile without moving product data into Access.
/// </summary>
public interface IBullgateAccessApplication<TRegistration, TApplication>
    where TRegistration : class
    where TApplication : class
{
    /// <summary>
    /// Idempotently creates or returns the product profile for the Bullgate
    /// identity and the validated registration payload.
    /// </summary>
    /// <param name="session">The Bullgate identity and session facts.</param>
    /// <param name="registration">The consumer-defined registration payload.</param>
    /// <param name="cancellationToken">Cancels local provisioning.</param>
    /// <returns>The committed consumer product profile.</returns>
    ValueTask<BullgateApplicationProvisionResult<TApplication>> ProvisionAsync(
        BullgateSession session,
        TRegistration registration,
        CancellationToken cancellationToken);

    /// <summary>Resolves the existing product profile, or returns null when none exists.</summary>
    /// <param name="session">The Bullgate identity and session facts.</param>
    /// <param name="cancellationToken">Cancels local resolution.</param>
    /// <returns>The consumer product profile, or null when it has not been provisioned.</returns>
    ValueTask<TApplication?> ResolveAsync(
        BullgateSession session,
        CancellationToken cancellationToken);
}

/// <summary>Combines Bullgate identity facts with a required consumer product profile.</summary>
/// <typeparam name="TApplication">The consumer-defined public product-profile type.</typeparam>
/// <param name="IdentityId">The stable Bullgate identity identifier.</param>
/// <param name="Email">The current normalized identity email.</param>
/// <param name="SessionExpiresAt">The absolute Bullgate session expiration.</param>
/// <param name="Application">The resolved consumer-owned product profile.</param>
public sealed record BullgateAccessEnvelope<TApplication>(
    Guid IdentityId,
    string Email,
    DateTimeOffset SessionExpiresAt,
    TApplication Application)
    where TApplication : class;

/// <summary>Defines stable serialized state names returned by the Access BFF.</summary>
public static class BullgateAccessSessionState
{
    /// <summary>A product-purpose session with a resolved local profile.</summary>
    public const string Authenticated = "authenticated";

    /// <summary>A valid identity session whose product registration is incomplete.</summary>
    public const string Registration = "registration";
}

/// <summary>
/// Combines identity-owned session facts with an optional consumer-owned product
/// profile. The profile is null for registration-purpose sessions.
/// </summary>
/// <typeparam name="TApplication">The consumer-defined public product-profile type.</typeparam>
/// <param name="State">The stable BFF session state name.</param>
/// <param name="IdentityId">The stable Bullgate identity identifier.</param>
/// <param name="Email">The current normalized identity email.</param>
/// <param name="Phone">The current identity phone, when present.</param>
/// <param name="PhoneVerifiedAt">When the current phone was proven, or null when unverified.</param>
/// <param name="SessionExpiresAt">The absolute Bullgate session expiration.</param>
/// <param name="HasPassword">Whether the identity has password authentication.</param>
/// <param name="HasGoogle">Whether the identity has a linked Google login.</param>
/// <param name="GoogleEmail">The provider email reported for the linked Google login.</param>
/// <param name="HasApple">Whether the identity has a linked Apple login.</param>
/// <param name="AppleEmail">The provider email reported for the linked Apple login.</param>
/// <param name="Application">The resolved product profile, or null during registration.</param>
public sealed record BullgateAccessSessionEnvelope<TApplication>(
    string State,
    Guid IdentityId,
    string Email,
    string? Phone,
    DateTimeOffset? PhoneVerifiedAt,
    DateTimeOffset SessionExpiresAt,
    bool HasPassword,
    bool HasGoogle,
    string? GoogleEmail,
    bool HasApple,
    string? AppleEmail,
    TApplication? Application)
    where TApplication : class;

/// <summary>Combines a public flow snapshot with a session established by the completed action.</summary>
/// <typeparam name="TApplication">The consumer-defined public product-profile type.</typeparam>
/// <param name="Flow">The current client-visible flow snapshot.</param>
/// <param name="Session">The product session established by completion, when present.</param>
public sealed record BullgateAccessFlowEnvelope<TApplication>(
    BullgateAccessFlowSnapshot Flow,
    BullgateAccessSessionEnvelope<TApplication>? Session = null)
    where TApplication : class;
