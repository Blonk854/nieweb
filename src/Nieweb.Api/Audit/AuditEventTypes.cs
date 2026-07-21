namespace Nieweb.Api.Audit;

/// <summary>
/// Stable, dot-separated event-type keys written to
/// <c>AuditEvents.EventType</c>. Consumers filter and group on these
/// strings so any rename is a breaking change to historical queries.
/// </summary>
public static class AuditEventTypes
{
    /// <summary>Admin created a local user account.</summary>
    public const string UserCreated = "user.created";

    /// <summary>Admin updated a user's display name / disabled flag / roles.</summary>
    public const string UserUpdated = "user.updated";

    /// <summary>Admin changed at least one role assignment.</summary>
    public const string UserRoleChanged = "user.role.changed";

    /// <summary>Admin reset a user's password out-of-band.</summary>
    public const string UserPasswordReset = "user.password.reset";

    /// <summary>OIDC sign-in auto-provisioned a new user.</summary>
    public const string UserOidcProvisioned = "user.oidc.provisioned";

    /// <summary>OIDC sign-in attached a new external-login binding to an existing user.</summary>
    public const string UserOidcLinked = "user.oidc.linked";

    /// <summary>OIDC sign-in was refused because a local account with the same email already exists.</summary>
    public const string OidcConflict = "user.oidc.conflict";

    /// <summary>Local username + password sign-in succeeded.</summary>
    public const string AuthSignInOk = "auth.signin.ok";

    /// <summary>Sign-in was refused (bad credentials, disabled account, forced-rotation gate, ...).</summary>
    public const string AuthSignInFailed = "auth.signin.failed";

    /// <summary>OIDC sign-in completed and a Nieweb JWT was issued.</summary>
    public const string AuthSsoSignInOk = "auth.sso.signin.ok";

    /// <summary>User changed their own password.</summary>
    public const string AuthPasswordChanged = "auth.password.changed";
}

/// <summary>
/// Stable target-type keys written to <c>AuditEvents.TargetType</c>.
/// </summary>
public static class AuditTargetTypes
{
    public const string User = "User";
    public const string Session = "Session";
    public const string SavedView = "SavedView";
}
