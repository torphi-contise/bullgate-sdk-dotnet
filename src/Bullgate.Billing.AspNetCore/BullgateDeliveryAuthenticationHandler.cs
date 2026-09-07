using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bullgate.Billing.AspNetCore;

/// <summary>Creates and validates random credentials for the entitlement delivery receiver.</summary>
public static class BullgateDeliveryCredential
{
    /// <summary>Generates a cryptographically random credential in the <c>bgbd_</c> format.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return "bgbd_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>Returns whether a value is a canonical credential with 256 bits of payload.</summary>
    public static bool IsValid(string? value)
    {
        if (value is not { Length: 48 } || !value.StartsWith("bgbd_", StringComparison.Ordinal)) return false;
        var payload = value[5..];
        if (payload.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        Span<byte> bytes = stackalloc byte[32];
        try
        {
            return Convert.TryFromBase64String(payload.Replace('-', '+').Replace('_', '/') + "=", bytes, out var written)
                && written == 32
                && Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') == payload;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

internal sealed class BullgateDeliveryAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemes, ILoggerFactory logger, UrlEncoder encoder,
    IOptions<BullgateEntitlementReceiverOptions> receiver)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    public const string SchemeName = "BullgateBillingDelivery";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;
        if (headers.Count != 1 || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(headers[0], out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || !BullgateDeliveryCredential.IsValid(header.Parameter))
            return Task.FromResult(AuthenticateResult.NoResult());

        var options = receiver.Value;
        var incoming = SHA256.HashData(Encoding.UTF8.GetBytes(header.Parameter!));
        try
        {
            var matches = false;
            foreach (var token in options.AcceptedCredentials)
            {
                var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                try { matches |= CryptographicOperations.FixedTimeEquals(incoming, expected); }
                finally { CryptographicOperations.ZeroMemory(expected); }
            }
            if (!matches) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("billing-environment-key", options.EnvironmentKey)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
        finally { CryptographicOperations.ZeroMemory(incoming); }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
