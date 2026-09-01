using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Auth;
using Nieweb.Api.Licensing;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for authentication:
/// <c>POST /auth/login</c>, <c>GET /auth/whoami</c>, and
/// <c>POST /auth/change-password</c>.
/// </summary>
public static partial class AuthEndpoints
{
    /// <summary>
    /// Registers the <c>/auth/*</c> endpoints on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithName("AuthLogin");

        group.MapGet("/whoami", WhoAmIAsync)
            .RequireAuthorization()
            .WithName("AuthWhoAmI");

        group.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization()
            .WithName("AuthChangePassword");

        // Public discovery endpoint used by the SPA to decide whether to
        // render the "Sign in with SSO" button on the login page (I2).
        // Kept anonymous so the login screen can render before any
        // credentials are supplied.
        group.MapGet("/config", GetConfig)
            .AllowAnonymous()
            .WithName("AuthConfig");

        return routes;
    }

    /// <summary>Public auth configuration for the SPA (see <c>GET /auth/config</c>).</summary>
    /// <param name="OidcEnabled">
    /// True when <see cref="Nieweb.Api.Auth.OidcOptions.Enabled"/> is set
    /// AND the required OIDC settings are populated. When false the SPA
    /// hides the SSO button entirely.
    /// </param>
    /// <param name="OidcButtonLabel">Human-readable label to render on the SSO button.</param>
    /// <param name="OidcChallengePath">Path the SPA should navigate to (top-level, not a fetch) to start the OIDC flow.</param>
    /// <param name="AnalyseEnabled">
    /// True when the Analyse license token is enabled on this host.
    /// </param>
    public sealed record AuthConfigResponse(
        bool OidcEnabled,
        string OidcButtonLabel,
        string OidcChallengePath,
        bool AnalyseEnabled);

    private static async Task<IResult> GetConfig(
        Microsoft.Extensions.Options.IOptionsMonitor<Nieweb.Api.Auth.OidcOptions> oidc,
        ILicenseTokens licenseTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oidc);
        ArgumentNullException.ThrowIfNull(licenseTokens);

        var opts = oidc.CurrentValue;
        var enabled = opts.Enabled
            && !string.IsNullOrWhiteSpace(opts.Authority)
            && !string.IsNullOrWhiteSpace(opts.ClientId);

        var analyseEnabled = await licenseTokens
            .IsEnabledAsync(LicenseTokenNames.Analyse, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new AuthConfigResponse(
            OidcEnabled: enabled,
            OidcButtonLabel: enabled ? opts.ButtonLabel : string.Empty,
            OidcChallengePath: enabled ? "/auth/oidc/challenge" : string.Empty,
            AnalyseEnabled: analyseEnabled));
    }

    /// <summary>Login request body.</summary>
    public sealed record LoginRequest
    {
        /// <summary>
        /// Login identifier: a registered email address <b>or</b> a
        /// username (both case-insensitive). The field is named
        /// <c>Email</c> for wire compatibility but is treated as an
        /// identifier — email lookup is tried first, then username.
        /// </summary>
        [Required]
        public string Email { get; init; } = string.Empty;

        /// <summary>Plain-text password. Never logged, never stored.</summary>
        [Required]
        public string Password { get; init; } = string.Empty;
    }

    /// <summary>Login response body.</summary>
    /// <param name="AccessToken">Signed JWT bearer token.</param>
    /// <param name="TokenType">Always <c>Bearer</c>.</param>
    /// <param name="ExpiresUtc">UTC instant the token stops being accepted.</param>
    /// <param name="MustRotatePassword">
    /// True if the account is flagged for forced password rotation
    /// (bootstrap admin, freshly-created accounts, admin-initiated
    /// resets). The SPA must send the user to the change-password
    /// screen before letting them reach any other route.
    /// </param>
    public sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTime ExpiresUtc,
        bool MustRotatePassword);

    /// <summary>Response body for <c>/auth/whoami</c>.</summary>
    /// <param name="UserId">The user's Identity primary key (as string).</param>
    /// <param name="Email">Primary email if present.</param>
    /// <param name="Name">Display name if present, else user name.</param>
    /// <param name="Roles">Roles carried by the current token.</param>
    /// <param name="MustRotatePassword">
    /// True if the account is flagged for forced password rotation.
    /// Mirrors <see cref="LoginResponse.MustRotatePassword"/> so a
    /// browser reload can rediscover the flag without keeping it in
    /// localStorage.
    /// </param>
    public sealed record WhoAmIResponse(
        string? UserId,
        string? Email,
        string? Name,
        IReadOnlyList<string> Roles,
        bool MustRotatePassword);

    /// <summary>Change-password request body.</summary>
    public sealed record ChangePasswordRequest
    {
        /// <summary>
        /// The user's current password. Required even when
        /// <see cref="NiewebUser.MustRotatePassword"/> is set, because
        /// we still want the current holder of the temporary password
        /// to be the one who picks the replacement.
        /// </summary>
        [Required, StringLength(256, MinimumLength = 1)]
        public string CurrentPassword { get; init; } = string.Empty;

        /// <summary>Replacement password. Runs through Identity validators.</summary>
        [Required, StringLength(256, MinimumLength = 1)]
        public string NewPassword { get; init; } = string.Empty;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        SignInManager<NiewebUser> signIn,
        UserManager<NiewebUser> users,
        IJwtTokenIssuer tokens,
        Audit.IAuditLog audit,
        Microsoft.Extensions.Options.IOptions<Auth.SecurityOptions> security,
        ILogger<LoginRequestMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(security);

        // Accept an email OR a username. Email is tried first (the common
        // case); usernames that happen to contain '@' still resolve via
        // the fallback.
        var user = await users.FindByEmailAsync(request.Email).ConfigureAwait(false)
            ?? await users.FindByNameAsync(request.Email).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            // Do not disclose whether the account exists.
            LogUnknownOrDisabled(logger);
            await audit.WriteAsync(
                Audit.AuditEventTypes.AuthSignInFailed,
                Audit.AuditTargetTypes.Session,
                user?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
                actorUserId: user?.Id,
                actorDisplayName: user?.DisplayName ?? request.Email,
                details: new
                {
                    email = request.Email,
                    reason = user is null ? "unknown-account" : "disabled",
                },
                cancellationToken).ConfigureAwait(false);
            return Results.Unauthorized();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
            .ConfigureAwait(false);
        if (result.IsLockedOut)
        {
            LogLockedOut(logger, user.Id);
            await audit.WriteAsync(
                Audit.AuditEventTypes.AuthSignInFailed,
                Audit.AuditTargetTypes.Session,
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                actorUserId: user.Id,
                actorDisplayName: user.DisplayName,
                details: new { email = user.Email, reason = "locked-out" },
                cancellationToken).ConfigureAwait(false);
            return Results.Unauthorized();
        }
        if (!result.Succeeded)
        {
            LogBadPassword(logger, user.Id);
            await audit.WriteAsync(
                Audit.AuditEventTypes.AuthSignInFailed,
                Audit.AuditTargetTypes.Session,
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                actorUserId: user.Id,
                actorDisplayName: user.DisplayName,
                details: new { email = user.Email, reason = "bad-password" },
                cancellationToken).ConfigureAwait(false);
            return Results.Unauthorized();
        }

        user.LastLoginUtc = DateTime.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);

        var roles = await users.GetRolesAsync(user).ConfigureAwait(false);
        var issued = tokens.Issue(user, (IReadOnlyCollection<string>)roles);

        LogGranted(logger, user.Id);
        // Relaxed-login mode leaves the MustRotatePassword column intact
        // but does not force the change on the client.
        var mustRotate = user.MustRotatePassword && !security.Value.RelaxedLogin;
        await audit.WriteAsync(
            Audit.AuditEventTypes.AuthSignInOk,
            Audit.AuditTargetTypes.Session,
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            actorUserId: user.Id,
            actorDisplayName: user.DisplayName,
            details: new { email = user.Email, mustRotatePassword = mustRotate, roles },
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(new LoginResponse(
            issued.AccessToken,
            "Bearer",
            issued.ExpiresUtc,
            mustRotate));
    }

    private static async Task<IResult> WhoAmIAsync(
        ClaimsPrincipal principal,
        UserManager<NiewebUser> users,
        Microsoft.Extensions.Options.IOptions<Auth.SecurityOptions> security)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(security);

        // The MustRotatePassword flag lives in the DB, not the JWT,
        // so a token issued just before an admin flips the flag stops
        // reflecting reality within one /auth/whoami roundtrip. This
        // keeps the token stateless (no need to re-issue) while still
        // letting the SPA discover the requirement on refresh.
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        var mustRotate = user?.MustRotatePassword ?? false;

        var response = new WhoAmIResponse(
            UserId: principal.FindFirstValue(ClaimTypes.NameIdentifier),
            Email: principal.FindFirstValue(ClaimTypes.Email),
            Name: principal.Identity?.Name,
            Roles: principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
            MustRotatePassword: mustRotate);
        return Results.Ok(response);
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<NiewebUser> users,
        Audit.IAuditLog audit,
        ILogger<LoginRequestMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            // Token was valid at request time but the account is gone
            // or disabled between issuance and this call. Same 401
            // shape as /auth/login so the SPA can treat it uniformly.
            return Results.Unauthorized();
        }

        var result = await users.ChangePasswordAsync(
                user,
                request.CurrentPassword,
                request.NewPassword)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.Description).ToArray(),
                    StringComparer.Ordinal);
            LogPasswordChangeRejected(logger, user.Id);
            return Results.ValidationProblem(errors);
        }

        user.MustRotatePassword = false;
        user.LastModifiedUtc = DateTime.UtcNow;
        _ = await users.UpdateAsync(user).ConfigureAwait(false);

        LogPasswordChanged(logger, user.Id);
        await audit.WriteAsync(
            Audit.AuditEventTypes.AuthPasswordChanged,
            Audit.AuditTargetTypes.User,
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new { email = user.Email },
            cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// Marker type used only to name the <see cref="ILogger{T}"/> category
    /// for <c>/auth/login</c> - keeps the log category short and stable.
    /// </summary>
    public sealed class LoginRequestMarker
    {
    }

    // Source-generated LoggerMessage delegates: allocation-free, honour
    // structured logging, and satisfy CA1848 / CA1873.

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Login rejected for unknown or disabled account")]
    private static partial void LogUnknownOrDisabled(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Login rejected: account is locked out (userId={UserId})")]
    private static partial void LogLockedOut(ILogger logger, int userId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Login rejected: bad password (userId={UserId})")]
    private static partial void LogBadPassword(ILogger logger, int userId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Login granted (userId={UserId})")]
    private static partial void LogGranted(ILogger logger, int userId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Password change rejected by validators (userId={UserId})")]
    private static partial void LogPasswordChangeRejected(ILogger logger, int userId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information,
        Message = "Password changed (userId={UserId})")]
    private static partial void LogPasswordChanged(ILogger logger, int userId);
}
