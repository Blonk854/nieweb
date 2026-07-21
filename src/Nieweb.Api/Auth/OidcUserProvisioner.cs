using System.Security.Claims;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

using Nieweb.Data.Entities;

namespace Nieweb.Api.Auth;

/// <summary>
/// Look-up-or-provision helper for OpenID Connect sign-ins. Kept as a
/// pure service (no HTTP dependencies) so the branching logic can be
/// exercised directly by unit tests, and so both the OIDC callback
/// endpoint and future ID-token bearer flows can share it.
/// </summary>
/// <remarks>
/// <para>Matches (in order):</para>
/// <list type="number">
///   <item><description>External login binding
///     (<see cref="UserManager{TUser}.FindByLoginAsync"/> against
///     provider + subject) — stable across email changes on the IdP.</description></item>
///   <item><description>Existing <see cref="NiewebUser.IsOidcProvisioned"/> user with the
///     same email — attaches the login binding on first sign-in from
///     a new provider or after a re-registration.</description></item>
///   <item><description>Existing <b>local</b> user with the same email — <b>rejected</b>
///     to avoid an SSO caller silently hijacking a local admin account.
///     An administrator must convert the local account to OIDC
///     explicitly (out-of-band tooling / future admin UI).</description></item>
///   <item><description>No match — provisions a brand-new user with
///     <see cref="NiewebUser.IsOidcProvisioned"/> = true, the configured
///     default role (typically <c>Reader</c>), and attaches the login
///     binding.</description></item>
/// </list>
/// </remarks>
public sealed class OidcUserProvisioner
{
    private readonly UserManager<NiewebUser> _users;
    private readonly TimeProvider _time;
    private static readonly Regex UserNameSanitizer = new("[^A-Za-z0-9_@.-]", RegexOptions.Compiled);

    /// <summary>Creates a new provisioner.</summary>
    public OidcUserProvisioner(UserManager<NiewebUser> users, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(time);
        _users = users;
        _time = time;
    }

    /// <summary>Result of <see cref="LookupOrProvisionAsync"/>.</summary>
    public sealed record Result(NiewebUser? User, ProvisionOutcome Outcome, string? Error);

    /// <summary>What happened during look-up-or-provision.</summary>
    public enum ProvisionOutcome
    {
        /// <summary>Existing user found via login-binding or email; signed in.</summary>
        ExistingSignedIn,

        /// <summary>New user auto-provisioned with the default role.</summary>
        Provisioned,

        /// <summary>User exists as a local account; refuse to hijack.</summary>
        LocalAccountConflict,

        /// <summary>Required claim missing (usually email).</summary>
        MissingRequiredClaim,

        /// <summary>Identity store rejected the create/attach.</summary>
        IdentityError,
    }

    /// <summary>
    /// Runs the look-up-or-provision flow for a <paramref name="principal"/>
    /// authenticated by <paramref name="loginProvider"/>. On success
    /// returns the user with roles hydrated; the caller is responsible
    /// for issuing the JWT.
    /// </summary>
    /// <param name="principal">Claims principal produced by the OIDC
    /// middleware (must carry a stable subject claim).</param>
    /// <param name="loginProvider">Login-binding provider key
    /// (e.g. <c>oidc</c>); combined with the subject claim to form the
    /// <see cref="IdentityUserLogin{TKey}"/> row.</param>
    /// <param name="defaultRole">Role assigned to newly provisioned
    /// users. Must exist in the role store; the caller enforces that.</param>
    public async Task<Result> LookupOrProvisionAsync(
        ClaimsPrincipal principal,
        string loginProvider,
        string defaultRole)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrEmpty(loginProvider);
        ArgumentException.ThrowIfNullOrEmpty(defaultRole);

        var subject = FindClaim(principal,
            JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(subject))
        {
            return new Result(null, ProvisionOutcome.MissingRequiredClaim,
                "Identity token did not include a 'sub' (subject) claim.");
        }

        var email = FindClaim(principal,
            JwtRegisteredClaimNames.Email, ClaimTypes.Email,
            "preferred_username", "upn");
        if (string.IsNullOrEmpty(email))
        {
            return new Result(null, ProvisionOutcome.MissingRequiredClaim,
                "Identity token did not include an email / preferred_username / upn claim.");
        }

        var displayName = FindClaim(principal,
            "name", ClaimTypes.Name, "preferred_username") ?? email;

        // 1) Login-binding lookup (stable across email changes).
        var bound = await _users.FindByLoginAsync(loginProvider, subject).ConfigureAwait(false);
        if (bound is not null)
        {
            if (bound.IsDisabled)
            {
                return new Result(null, ProvisionOutcome.LocalAccountConflict,
                    "This account has been disabled.");
            }
            await TouchLastLoginAsync(bound).ConfigureAwait(false);
            return new Result(bound, ProvisionOutcome.ExistingSignedIn, null);
        }

        // 2) OIDC user with matching email — attach binding.
        var byEmail = await _users.FindByEmailAsync(email).ConfigureAwait(false);
        if (byEmail is not null && byEmail.IsOidcProvisioned)
        {
            if (byEmail.IsDisabled)
            {
                return new Result(null, ProvisionOutcome.LocalAccountConflict,
                    "This account has been disabled.");
            }
            var addLogin = await _users.AddLoginAsync(byEmail,
                new UserLoginInfo(loginProvider, subject, loginProvider)).ConfigureAwait(false);
            if (!addLogin.Succeeded)
            {
                return new Result(null, ProvisionOutcome.IdentityError,
                    string.Join("; ", addLogin.Errors.Select(e => e.Description)));
            }
            await TouchLastLoginAsync(byEmail).ConfigureAwait(false);
            return new Result(byEmail, ProvisionOutcome.ExistingSignedIn, null);
        }

        // 3) Local (non-OIDC) account with matching email — refuse.
        if (byEmail is not null && !byEmail.IsOidcProvisioned)
        {
            return new Result(null, ProvisionOutcome.LocalAccountConflict,
                $"Email '{email}' is already registered as a local account. "
                + "An administrator must convert it to SSO before you can sign in this way.");
        }

        // 4) Auto-provision.
        var now = _time.GetUtcNow().UtcDateTime;
        var user = new NiewebUser
        {
            UserName = SanitizeUserName(email),
            Email = email,
            EmailConfirmed = true, // trust the IdP's email verification
            DisplayName = displayName,
            IsOidcProvisioned = true,
            MustRotatePassword = false,
            CreatedUtc = now,
            LastModifiedUtc = now,
            LastLoginUtc = now,
        };
        var create = await _users.CreateAsync(user).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            return new Result(null, ProvisionOutcome.IdentityError,
                string.Join("; ", create.Errors.Select(e => e.Description)));
        }
        var addRole = await _users.AddToRoleAsync(user, defaultRole).ConfigureAwait(false);
        if (!addRole.Succeeded)
        {
            return new Result(null, ProvisionOutcome.IdentityError,
                string.Join("; ", addRole.Errors.Select(e => e.Description)));
        }
        var addLoginNew = await _users.AddLoginAsync(user,
            new UserLoginInfo(loginProvider, subject, loginProvider)).ConfigureAwait(false);
        if (!addLoginNew.Succeeded)
        {
            return new Result(null, ProvisionOutcome.IdentityError,
                string.Join("; ", addLoginNew.Errors.Select(e => e.Description)));
        }
        return new Result(user, ProvisionOutcome.Provisioned, null);
    }

    private async Task TouchLastLoginAsync(NiewebUser user)
    {
        user.LastLoginUtc = _time.GetUtcNow().UtcDateTime;
        await _users.UpdateAsync(user).ConfigureAwait(false);
    }

    private static string? FindClaim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var t in types)
        {
            var value = principal.FindFirst(t)?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>
    /// ASP.NET Core Identity restricts usernames to the alphanumeric set
    /// defined by <c>Nieweb:Identity:User:AllowedUserNameCharacters</c>
    /// (defaults to letters, digits, and <c>-._@+</c>). OIDC emails
    /// are almost always compatible, but we strip any stray characters
    /// defensively so the create call cannot fail on a Unicode local
    /// part.
    /// </summary>
    private static string SanitizeUserName(string email) =>
        UserNameSanitizer.Replace(email, "_");
}
