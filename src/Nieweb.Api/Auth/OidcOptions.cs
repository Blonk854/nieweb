namespace Nieweb.Api.Auth;

/// <summary>
/// OpenID Connect / OAuth 2.0 configuration for the Nieweb API. Bound
/// from configuration section <c>Nieweb:Auth:Oidc</c>. Backlog item
/// <c>I2</c> in <c>docs/phase-1-mvp.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// The feature is <b>opt-in</b>: <see cref="Enabled"/> defaults to
/// <see langword="false"/> so a fresh install (or a dev box without an
/// Entra tenant) ships with only local accounts. When
/// <see cref="Enabled"/> is <see langword="true"/>, the API adds an
/// OpenID Connect authentication scheme alongside the existing JWT
/// bearer scheme; the SPA discovers this via <c>GET /auth/config</c>
/// and renders a "Sign in with SSO" button on the login page.
/// </para>
/// <para>
/// The provider keys below use OIDC-standard names rather than
/// Entra-specific ones (<c>Instance</c>, <c>TenantId</c>, ...) so the
/// same wiring works against Microsoft Entra ID, AD FS, Keycloak,
/// Okta, or any OpenID-compliant IdP. For a typical Entra
/// registration, <see cref="Authority"/> is
/// <c>https://login.microsoftonline.com/{tenant}/v2.0</c>.
/// </para>
/// <para>
/// <b>Secrets</b> (<see cref="ClientSecret"/>) must be supplied via
/// environment variable (<c>NIEWEB__AUTH__OIDC__CLIENTSECRET</c>) or
/// a secret store, never committed to source control.
/// </para>
/// </remarks>
public sealed class OidcOptions
{
    /// <summary>Configuration section root: <c>Nieweb:Auth:Oidc</c>.</summary>
    public const string SectionName = "Nieweb:Auth:Oidc";

    /// <summary>
    /// Feature flag. When <see langword="false"/> (default) the OIDC
    /// scheme is not registered, <c>/auth/oidc/*</c> endpoints return
    /// 404, and <c>GET /auth/config</c> reports SSO as unavailable.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// OIDC authority (discovery-document base URL). Required when
    /// <see cref="Enabled"/> is <see langword="true"/>. Example for
    /// Entra ID single-tenant:
    /// <c>https://login.microsoftonline.com/{tenantId}/v2.0</c>.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Client ID (a.k.a. Application ID) as registered with the IdP.
    /// Required when <see cref="Enabled"/> is <see langword="true"/>.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret / application password. Required when
    /// <see cref="Enabled"/> is <see langword="true"/>. Never commit to
    /// source control; supply via env var or a secret store.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Space-delimited scopes to request. Defaults to the minimal set
    /// needed to identify the user (<c>openid profile email</c>).
    /// </summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>
    /// Path (on the Nieweb host) that the IdP redirects back to after
    /// authentication. Must match the redirect URI configured on the
    /// IdP side. Defaults to <c>/signin-oidc</c>.
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// Path (on the Nieweb host) that the IdP redirects to after
    /// sign-out. Defaults to <c>/signout-callback-oidc</c>.
    /// </summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>
    /// Role auto-assigned to a user provisioned on their first OIDC
    /// sign-in. Defaults to <c>Reader</c> (kept as a plain string here
    /// to avoid pulling <c>Nieweb.Identity</c> into the options layer).
    /// </summary>
    /// <remarks>Matches one of the built-in roles seeded by <c>BootstrapAdmin</c>: <c>Reader</c>, <c>Author</c>, <c>Admin</c>.</remarks>
    public string DefaultRole { get; set; } = "Reader";

    /// <summary>
    /// Human-readable label rendered on the SPA's "Sign in with ..."
    /// button. Defaults to <c>Single sign-on</c> but ops typically
    /// customise this to <c>Sign in with Entra ID</c> or similar.
    /// </summary>
    public string ButtonLabel { get; set; } = "Single sign-on";
}
