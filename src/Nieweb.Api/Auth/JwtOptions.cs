namespace Nieweb.Api.Auth;

/// <summary>
/// JWT bearer token configuration for the Nieweb API. Bound from
/// configuration section <c>Nieweb:Auth:Jwt</c>.
/// </summary>
/// <remarks>
/// The signing key is a symmetric secret used to sign HS256 tokens.
/// It must be at least 32 UTF-8 bytes long (the minimum size accepted
/// by <c>Microsoft.IdentityModel.Tokens.SymmetricSecurityKey</c> for
/// HMAC-SHA256). In production it must be supplied via environment
/// variable or a secret store, never committed to source control.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Token <c>iss</c> claim. Also validated on incoming tokens.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Token <c>aud</c> claim. Also validated on incoming tokens.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric secret used to sign and validate tokens. Minimum
    /// 32 UTF-8 bytes for HS256.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long an access token remains valid after issuance.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Clock skew tolerated during token validation. Defaults to 30 s
    /// (Microsoft.IdentityModel's default is 5 minutes, which is too
    /// permissive for an API where all clocks should be NTP-synced).
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
