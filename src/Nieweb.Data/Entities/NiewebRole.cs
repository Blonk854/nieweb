using Microsoft.AspNetCore.Identity;

namespace Nieweb.Data.Entities;

/// <summary>
/// Application role. Extends <see cref="IdentityRole{TKey}"/> with a
/// human-readable description.
/// </summary>
/// <remarks>
/// Nieweb ships with three built-in roles: <c>Reader</c>, <c>Author</c>,
/// <c>Admin</c> (matches the legacy Vieweb role taxonomy). Additional
/// roles can be created by admins.
/// </remarks>
public sealed class NiewebRole : IdentityRole<int>
{
    /// <summary>
    /// Optional description shown in the role-management UI.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// True for the three built-in roles - prevents deletion or rename.
    /// </summary>
    public bool IsBuiltIn { get; set; }
}
