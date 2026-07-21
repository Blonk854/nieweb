using System.Security.Claims;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Nieweb.Data.Entities;

namespace Nieweb.Api.Auth;

/// <summary>
/// Issues signed JWT bearer tokens for authenticated users.
/// </summary>
public interface IJwtTokenIssuer
{
    /// <summary>
    /// Creates a signed access token for <paramref name="user"/> that
    /// carries the standard identity claims plus each of
    /// <paramref name="roles"/> as a <see cref="ClaimTypes.Role"/> claim.
    /// </summary>
    /// <param name="user">Authenticated user.</param>
    /// <param name="roles">Roles the user belongs to.</param>
    /// <returns>The compact JWT and its UTC expiry.</returns>
    IssuedToken Issue(NiewebUser user, IReadOnlyCollection<string> roles);
}

/// <summary>Result of <see cref="IJwtTokenIssuer.Issue"/>.</summary>
/// <param name="AccessToken">Compact-serialized JWT (three base64url parts).</param>
/// <param name="ExpiresUtc">UTC instant at which the token stops being valid.</param>
public sealed record IssuedToken(string AccessToken, DateTime ExpiresUtc);

/// <summary>
/// Default <see cref="IJwtTokenIssuer"/> that produces HS256-signed tokens
/// using the symmetric key configured in <see cref="JwtOptions"/>.
/// </summary>
public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly IOptionsMonitor<JwtOptions> _options;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates a new <see cref="JwtTokenIssuer"/>.
    /// </summary>
    /// <param name="options">Signing options; monitored so key rotations
    /// take effect without restart.</param>
    /// <param name="time">Clock used to stamp <c>nbf</c>/<c>exp</c>.</param>
    public JwtTokenIssuer(IOptionsMonitor<JwtOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _options = options;
        _time = time;
    }

    /// <inheritdoc/>
    public IssuedToken Issue(NiewebUser user, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var opts = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;
        var expires = now.Add(opts.AccessTokenLifetime);

        var claims = new List<Claim>(capacity: 5 + roles.Count)
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, string.IsNullOrEmpty(user.DisplayName) ? user.UserName ?? string.Empty : user.DisplayName),
        };
        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opts.Issuer,
            Audience = opts.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims, authenticationType: "jwt"),
            SigningCredentials = credentials,
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new IssuedToken(token, expires);
    }
}
