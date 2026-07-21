using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Nieweb.Api.Auth;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Endpoint group for the OpenID Connect / SSO sign-in flow
/// (backlog item <c>I2</c> in <c>docs/phase-1-mvp.md</c>).
/// </summary>
/// <remarks>
/// <para>Flow overview:</para>
/// <list type="number">
///   <item><description>SPA calls <c>GET /auth/oidc/challenge?returnUrl=/app/</c>.</description></item>
///   <item><description>Server issues an OIDC <c>ChallengeAsync</c>; browser is
///     redirected to the IdP.</description></item>
///   <item><description>User authenticates on the IdP; IdP redirects browser
///     back to <see cref="OidcOptions.CallbackPath"/> (default
///     <c>/signin-oidc</c>). The OIDC middleware handles that
///     transparently and stashes the sign-in principal in a short-lived
///     cookie (<see cref="CookieAuthenticationDefaults.AuthenticationScheme"/>).</description></item>
///   <item><description>Middleware finally redirects to
///     <c>GET /auth/oidc/callback-return?returnUrl=...</c> where this
///     endpoint looks up (or provisions) the user, issues a JWT bearer
///     token, clears the temporary cookie, and redirects the browser to
///     <c>/app/oidc-return#accessToken=...&amp;expiresUtc=...&amp;returnUrl=...</c>.
///     The SPA's <c>oidc-return</c> route strips the fragment, hydrates
///     the session store, and navigates onward.</description></item>
/// </list>
/// <para>
/// The URL-fragment handoff is used deliberately: the fragment is never
/// sent to the server (so the JWT does not appear in access logs) and
/// is readable by the SPA on load. This lets the entire API stay
/// stateless JWT-authenticated — no new session cookies bleed into
/// existing endpoints.
/// </para>
/// </remarks>
public static partial class OidcEndpoints
{
    /// <summary>Registers the <c>/auth/oidc/*</c> endpoint group.</summary>
    public static IEndpointRouteBuilder MapOidcEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var group = routes.MapGroup("/auth/oidc").WithTags("Auth");

        group.MapGet("/challenge", ChallengeAsync)
            .AllowAnonymous()
            .WithName("OidcChallenge");

        group.MapGet("/callback-return", CallbackReturnAsync)
            .WithName("OidcCallbackReturn");

        return routes;
    }

    /// <summary>Marker type for the endpoint logger category.</summary>
    public sealed class Marker
    {
    }

    /// <summary>
    /// Kicks off the OIDC challenge. Accepts a whitelisted local return
    /// URL (<c>/app/...</c>) that the SPA wants to land on after
    /// sign-in; anything else is silently coerced to <c>/app/</c> so
    /// that a crafted <c>returnUrl</c> cannot bounce the user to an
    /// external site (open-redirect defence).
    /// </summary>
    private static Task ChallengeAsync(
        HttpContext context,
        string? returnUrl,
        IOptionsMonitor<OidcOptions> oidcOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(oidcOptions);

        if (!oidcOptions.CurrentValue.Enabled)
        {
            // OIDC is off - behave as if the endpoint doesn't exist so
            // a scanner can't distinguish a Nieweb host that has SSO
            // configured from one that doesn't.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        var safeReturn = NormaliseReturnUrl(returnUrl);

        // Ask the OIDC middleware to redirect the browser to the IdP,
        // and have it redirect us back to our own callback-return
        // handler (not the raw OIDC callback path, which only sets up
        // the cookie principal without provisioning).
        var redirectAfterOidc = QueryHelpers("/auth/oidc/callback-return",
            ("returnUrl", safeReturn));
        var props = new AuthenticationProperties { RedirectUri = redirectAfterOidc };
        return context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, props);
    }

    private static async Task CallbackReturnAsync(
        HttpContext context,
        string? returnUrl,
        OidcUserProvisioner provisioner,
        IJwtTokenIssuer tokenIssuer,
        UserManager<NiewebUser> users,
        IOptionsMonitor<OidcOptions> oidcOptions,
        Audit.IAuditLog audit,
        ILogger<Marker> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(tokenIssuer);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(oidcOptions);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(logger);

        if (!oidcOptions.CurrentValue.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The OIDC middleware stashes the principal in a temporary
        // cookie (CookieAuthenticationDefaults.AuthenticationScheme).
        // Authenticate against it explicitly - we do NOT put
        // [Authorize] on the endpoint because the site default scheme
        // is JwtBearer.
        var authResult = await context
            .AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            LogNoCookiePrincipal(logger);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Not signed in via OIDC.").ConfigureAwait(false);
            return;
        }

        var opts = oidcOptions.CurrentValue;
        var result = await provisioner.LookupOrProvisionAsync(
            authResult.Principal,
            loginProvider: "oidc",
            defaultRole: opts.DefaultRole)
            .ConfigureAwait(false);

        if (result.User is null)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
#pragma warning disable CA1873 // ToString call is guarded by IsEnabled above.
                LogProvisioningFailed(logger, result.Outcome.ToString(), result.Error ?? string.Empty);
#pragma warning restore CA1873
            }
            // Ditch the transient cookie so a retry starts clean.
            await context
                .SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
            // Audit the failed provisioning attempt. The IdP-supplied
            // subject + email are the only identifiers we have — no
            // Nieweb user exists yet, so ActorUserId stays null.
            var attemptedEmail = authResult.Principal.FindFirst("email")?.Value
                ?? authResult.Principal.FindFirst("preferred_username")?.Value
                ?? authResult.Principal.FindFirst("upn")?.Value
                ?? string.Empty;
            var subject = authResult.Principal.FindFirst("sub")?.Value
                ?? authResult.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? string.Empty;
            await audit.WriteAsync(
                Audit.AuditEventTypes.OidcConflict,
                Audit.AuditTargetTypes.User,
                subject,
                actorUserId: null,
                actorDisplayName: attemptedEmail.Length > 0 ? attemptedEmail : "unknown",
                details: new
                {
                    outcome = result.Outcome.ToString(),
                    error = result.Error,
                    attemptedEmail,
                    loginProvider = "oidc",
                }).ConfigureAwait(false);
            var reason = Uri.EscapeDataString(result.Outcome.ToString());
            var errorMsg = Uri.EscapeDataString(result.Error ?? "OIDC sign-in failed.");
            context.Response.Redirect(
                $"/app/oidc-return#error={reason}&message={errorMsg}");
            return;
        }

        var roles = await users.GetRolesAsync(result.User).ConfigureAwait(false);
        var issued = tokenIssuer.Issue(result.User, (IReadOnlyCollection<string>)roles);

        // Cookie was only ever a handoff channel from OIDC middleware.
        await context
            .SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);

        if (logger.IsEnabled(LogLevel.Information))
        {
#pragma warning disable CA1873 // ToString call is guarded by IsEnabled above.
            LogSignInOk(logger, result.User.Id, result.Outcome.ToString(), result.User.Email ?? string.Empty);
#pragma warning restore CA1873
        }

        // Audit trail: distinguish first-ever OIDC provisioning from a
        // returning SSO sign-in (and further split returning-existing
        // by whether a fresh login binding was attached). Every path
        // also emits the generic auth.sso.signin.ok row so a filter on
        // that key alone captures every SSO sign-in.
        var userIdStr = result.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var actorName = result.User.DisplayName;
        if (result.Outcome == OidcUserProvisioner.ProvisionOutcome.Provisioned)
        {
            await audit.WriteAsync(
                Audit.AuditEventTypes.UserOidcProvisioned,
                Audit.AuditTargetTypes.User,
                userIdStr,
                actorUserId: result.User.Id,
                actorDisplayName: actorName,
                details: new
                {
                    email = result.User.Email,
                    displayName = result.User.DisplayName,
                    role = oidcOptions.CurrentValue.DefaultRole,
                }).ConfigureAwait(false);
        }
        await audit.WriteAsync(
            Audit.AuditEventTypes.AuthSsoSignInOk,
            Audit.AuditTargetTypes.Session,
            userIdStr,
            actorUserId: result.User.Id,
            actorDisplayName: actorName,
            details: new
            {
                email = result.User.Email,
                outcome = result.Outcome.ToString(),
                roles,
            }).ConfigureAwait(false);

        var safeReturn = NormaliseReturnUrl(returnUrl);
        var expiresIso = issued.ExpiresUtc.ToString("O", CultureInfo.InvariantCulture);
        // URL fragment is not sent to the server; keeps the JWT out
        // of every access log and reverse-proxy hop.
        var fragment = new StringBuilder(256)
            .Append("accessToken=").Append(Uri.EscapeDataString(issued.AccessToken))
            .Append("&expiresUtc=").Append(Uri.EscapeDataString(expiresIso))
            .Append("&mustRotatePassword=").Append(result.User.MustRotatePassword ? "true" : "false")
            .Append("&returnUrl=").Append(Uri.EscapeDataString(safeReturn))
            .ToString();
        context.Response.Redirect($"/app/oidc-return#{fragment}");
    }

    /// <summary>
    /// Constrains <paramref name="returnUrl"/> to local SPA paths so
    /// this endpoint cannot be abused as an open redirect. Anything
    /// starting with a scheme, backslash, or double slash is discarded.
    /// </summary>
    public static string NormaliseReturnUrl(string? returnUrl)
    {
        const string fallback = "/app/";
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return fallback;
        }
        // Reject anything that could resolve off-origin.
        if (returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith('\\')
            || returnUrl.Contains(':', StringComparison.Ordinal))
        {
            return fallback;
        }
        if (!returnUrl.StartsWith('/'))
        {
            return fallback;
        }
        // Only allow SPA paths. Everything server-side (e.g. /auth/*)
        // is fine to serve but never a useful post-sign-in destination.
        if (!returnUrl.StartsWith("/app", StringComparison.Ordinal))
        {
            return fallback;
        }
        return returnUrl;
    }

    // Tiny inline QueryString builder so we don't take a dependency on
    // WebUtilities just for two params.
    private static string QueryHelpers(string path, params (string Key, string Value)[] pairs)
    {
        var sb = new StringBuilder(path);
        var sep = '?';
        foreach (var (k, v) in pairs)
        {
            sb.Append(sep).Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
            sep = '&';
        }
        return sb.ToString();
    }

    // Source-generated LoggerMessage delegates: allocation-free, honour
    // structured logging, and satisfy CA1848 / CA1873.

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "OIDC callback-return invoked without an authenticated cookie principal")]
    private static partial void LogNoCookiePrincipal(ILogger logger);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "OIDC provisioning failed: outcome={Outcome} error={Error}")]
    private static partial void LogProvisioningFailed(ILogger logger, string outcome, string error);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information,
        Message = "OIDC sign-in ok: userId={UserId} outcome={Outcome} email={Email}")]
    private static partial void LogSignInOk(ILogger logger, int userId, string outcome, string email);
}
