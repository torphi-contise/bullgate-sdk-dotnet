namespace Bullgate.Access.AspNetCore;

/// <summary>Defines stable defaults and identifiers used by the Access adapter.</summary>
public static class BullgateAccessDefaults
{
    /// <summary>The ASP.NET Core authentication scheme name.</summary>
    public const string AuthenticationScheme = "Bullgate.Access";
    /// <summary>The route prefix used by the Access BFF and flow cookie.</summary>
    public const string RoutePrefix = "/bullgate/access/v1";
    /// <summary>The AccessFlow protocol version supported by this SDK.</summary>
    public const int AccessFlowProtocolVersion = 1;
    /// <summary>The intent used to continue product registration.</summary>
    public const string ContinueRegistrationIntent = "continueRegistration";
    /// <summary>The claim containing the stable Bullgate identity identifier.</summary>
    public const string IdentityIdClaim = "bullgate:identity_id";
    /// <summary>The claim containing the current Bullgate session identifier.</summary>
    public const string SessionIdClaim = "bullgate:session_id";
}
