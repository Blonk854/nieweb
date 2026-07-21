using Microsoft.AspNetCore.Identity;

namespace Nieweb.Data.Entities;

/// <summary>
/// Application user. Extends <see cref="IdentityUser{TKey}"/> with the
/// domain properties Nieweb needs on top of the standard ASP.NET Core
/// Identity columns (login/password/lockout/2FA/etc.).
/// </summary>
public sealed class NiewebUser : IdentityUser<int>
{
    /// <summary>
    /// Human-friendly name shown in the UI, audit log, and email
    /// notifications. Populated from OIDC <c>name</c> claim on first
    /// sign-in for federated users, or set by the admin for local users.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Set by an admin to soft-disable an account without deleting it.
    /// Disabled users cannot sign in but their audit history is preserved.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// True if this user was auto-provisioned via OIDC (Entra ID / AD FS).
    /// Local accounts created by an admin are false.
    /// </summary>
    public bool IsOidcProvisioned { get; set; }

    /// <summary>
    /// UTC timestamp of the last successful sign-in. Null until first
    /// login.
    /// </summary>
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the user record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent modification to any user column.
    /// </summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// Concurrency token to detect stale edits in the admin UI.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }
}
