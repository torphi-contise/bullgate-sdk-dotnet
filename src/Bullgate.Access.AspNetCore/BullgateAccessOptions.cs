using Microsoft.AspNetCore.Http;

namespace Bullgate.Access.AspNetCore;

/// <summary>Configures the Bullgate Access client and adapter-owned cookies.</summary>
public sealed class BullgateAccessOptions
{
    /// <summary>Gets or sets the absolute Bullgate Access service base address.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Gets or sets the server-only <c>bgic_</c> integration credential.</summary>
    public string IntegrationCredential { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout for each Access request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the host-only session cookie name.</summary>
    public string CookieName { get; set; } = "bullgate.session";

    /// <summary>Gets or sets the host-only AccessFlow capability cookie name.</summary>
    public string FlowCookieName { get; set; } = "bullgate.flow";

    /// <summary>Gets or sets the secure transport policy applied to both cookies.</summary>
    public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;

    /// <summary>Gets or sets the SameSite policy applied to both cookies.</summary>
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.Lax;
}
