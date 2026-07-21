using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Startup;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only endpoint group for managing local user accounts. Backed
/// by ASP.NET Core Identity (<see cref="UserManager{TUser}"/>) and gated
/// by the <c>Admin</c> role.
///
/// Nieweb never physically deletes users: an operator's audit trail
/// (saved views, sign-ins) must remain queryable long after they leave
/// the team. Removal is expressed as soft-disable (<see cref="NiewebUser.IsDisabled"/>).
///
/// Ground rules enforced here:
/// <list type="bullet">
///   <item><description>Roles must be one of <c>Reader</c>, <c>Author</c>, <c>Admin</c>.</description></item>
///   <item><description>The last remaining <c>Admin</c> cannot be disabled or demoted.</description></item>
///   <item><description>An admin cannot disable their own account (belt-and-braces).</description></item>
/// </list>
/// </summary>
public static partial class AdminUsersEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminUsersMarker;

    /// <summary>
    /// Registers the <c>/api/admin/users</c> endpoints on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/users")
            .WithTags("AdminUsers")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminUsersList");
        group.MapGet("/{id:int}", GetAsync).WithName("AdminUsersGet");
        group.MapPost(string.Empty, CreateAsync).WithName("AdminUsersCreate");
        group.MapPut("/{id:int}", UpdateAsync).WithName("AdminUsersUpdate");
        group.MapPost("/{id:int}/reset-password", ResetPasswordAsync)
            .WithName("AdminUsersResetPassword");

        return routes;
    }

    /// <summary>Row DTO returned by list + get.</summary>
    public sealed record AdminUserDto(
        int Id,
        string Email,
        string DisplayName,
        bool IsDisabled,
        bool IsOidcProvisioned,
        IReadOnlyList<string> Roles,
        DateTime CreatedUtc,
        DateTime? LastLoginUtc);

    /// <summary>Create-user request body.</summary>
    public sealed record CreateUserRequest
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; init; } = string.Empty;

        [Required, StringLength(200, MinimumLength = 1)]
        public string DisplayName { get; init; } = string.Empty;

        [Required, StringLength(256, MinimumLength = 1)]
        public string Password { get; init; } = string.Empty;

        /// <summary>
        /// Zero or more of <c>Reader</c>, <c>Author</c>, <c>Admin</c>.
        /// An empty list means the user has no role assignments and
        /// can sign in but cannot access any authorised endpoint.
        /// </summary>
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    }

    /// <summary>Update-user request body.</summary>
    public sealed record UpdateUserRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string DisplayName { get; init; } = string.Empty;

        public bool IsDisabled { get; init; }

        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    }

    /// <summary>Reset-password request body.</summary>
    public sealed record ResetPasswordRequest
    {
        [Required, StringLength(256, MinimumLength = 1)]
        public string NewPassword { get; init; } = string.Empty;
    }

    private static async Task<Ok<IReadOnlyList<AdminUserDto>>> ListAsync(
        UserManager<NiewebUser> users,
        CancellationToken cancellationToken)
    {
        var rows = await users.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.IsDisabled,
                u.IsOidcProvisioned,
                u.CreatedUtc,
                u.LastLoginUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Roles are stored in a join table; fetch them per user via
        // UserManager so the mapping honours normalized-name semantics.
        // The list is short (admin panel is small-team scope) so we
        // avoid a hand-rolled join.
        var dtos = new List<AdminUserDto>(rows.Count);
        foreach (var r in rows)
        {
            var user = await users.FindByIdAsync(r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
            var roles = user is null
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)await users.GetRolesAsync(user).ConfigureAwait(false);
            dtos.Add(new AdminUserDto(
                r.Id,
                r.Email ?? string.Empty,
                r.DisplayName,
                r.IsDisabled,
                r.IsOidcProvisioned,
                roles,
                r.CreatedUtc,
                r.LastLoginUtc));
        }
        return TypedResults.Ok((IReadOnlyList<AdminUserDto>)dtos);
    }

    private static async Task<Results<Ok<AdminUserDto>, NotFound>> GetAsync(
        int id,
        UserManager<NiewebUser> users,
        CancellationToken cancellationToken)
    {
        var user = await users.Users
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.NotFound();
        }
        var roles = (IReadOnlyList<string>)await users.GetRolesAsync(user).ConfigureAwait(false);
        return TypedResults.Ok(new AdminUserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.IsDisabled,
            user.IsOidcProvisioned,
            roles,
            user.CreatedUtc,
            user.LastLoginUtc));
    }

    private static async Task<Results<Created<AdminUserDto>, ValidationProblem, Conflict<string>>> CreateAsync(
        [FromBody] CreateUserRequest request,
        UserManager<NiewebUser> users,
        ILogger<AdminUsersMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rolesError = ValidateRoles(request.Roles);
        if (rolesError is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Roles"] = new[] { rolesError },
            });
        }

        var existing = await users.FindByEmailAsync(request.Email).ConfigureAwait(false);
        if (existing is not null)
        {
            return TypedResults.Conflict($"A user with email '{request.Email}' already exists.");
        }

        var now = DateTime.UtcNow;
        var user = new NiewebUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName,
            CreatedUtc = now,
            LastModifiedUtc = now,
            IsOidcProvisioned = false,
            // Admin-created accounts always ship with a temporary
            // password the operator communicates out-of-band. Force a
            // rotation on the user's first sign-in so that value
            // stops being valid after they log in once.
            MustRotatePassword = true,
        };

        var create = await users.CreateAsync(user, request.Password).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            return TypedResults.ValidationProblem(ToProblemDict(create));
        }

        if (request.Roles.Count > 0)
        {
            var addRoles = await users.AddToRolesAsync(user, request.Roles).ConfigureAwait(false);
            if (!addRoles.Succeeded)
            {
                // Roll back: creating the user then failing to grant roles
                // would leave a half-provisioned account. Deleting the
                // freshly-created record keeps the operation atomic from
                // the caller's perspective.
                await users.DeleteAsync(user).ConfigureAwait(false);
                return TypedResults.ValidationProblem(ToProblemDict(addRoles));
            }
        }

        LogUserCreated(logger, user.Email!, user.Id);
        var dto = new AdminUserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.IsDisabled,
            user.IsOidcProvisioned,
            request.Roles.ToArray(),
            user.CreatedUtc,
            user.LastLoginUtc);
        return TypedResults.Created($"/api/admin/users/{user.Id}", dto);
    }

    private static async Task<Results<Ok<AdminUserDto>, NotFound, ValidationProblem, Conflict<string>>> UpdateAsync(
        int id,
        [FromBody] UpdateUserRequest request,
        UserManager<NiewebUser> users,
        ClaimsPrincipal caller,
        ILogger<AdminUsersMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        var rolesError = ValidateRoles(request.Roles);
        if (rolesError is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Roles"] = new[] { rolesError },
            });
        }

        var user = await users.Users
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        var callerId = caller.FindFirstValue(ClaimTypes.NameIdentifier);
        var isSelf = int.TryParse(callerId, System.Globalization.CultureInfo.InvariantCulture, out var cid)
            && cid == id;

        // Guard: last remaining admin must not be disabled or demoted.
        var currentRoles = await users.GetRolesAsync(user).ConfigureAwait(false);
        var wasAdmin = currentRoles.Contains(BootstrapAdmin.RoleAdmin);
        var willBeAdmin = request.Roles.Contains(BootstrapAdmin.RoleAdmin);
        if (wasAdmin && (!willBeAdmin || request.IsDisabled))
        {
            var adminsRemaining = (await users.GetUsersInRoleAsync(BootstrapAdmin.RoleAdmin)
                .ConfigureAwait(false))
                .Count(u => !u.IsDisabled && u.Id != user.Id);
            if (adminsRemaining == 0)
            {
                return TypedResults.Conflict(
                    "Cannot disable or demote the last active Admin. Grant Admin to another user first.");
            }
        }

        // Guard: admins cannot disable themselves via this endpoint.
        if (isSelf && request.IsDisabled)
        {
            return TypedResults.Conflict("Administrators cannot disable their own account.");
        }

        user.DisplayName = request.DisplayName;
        user.IsDisabled = request.IsDisabled;
        user.LastModifiedUtc = DateTime.UtcNow;

        var update = await users.UpdateAsync(user).ConfigureAwait(false);
        if (!update.Succeeded)
        {
            return TypedResults.ValidationProblem(ToProblemDict(update));
        }

        var toRemove = currentRoles.Except(request.Roles, StringComparer.OrdinalIgnoreCase).ToArray();
        var toAdd = request.Roles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (toRemove.Length > 0)
        {
            var remove = await users.RemoveFromRolesAsync(user, toRemove).ConfigureAwait(false);
            if (!remove.Succeeded)
            {
                return TypedResults.ValidationProblem(ToProblemDict(remove));
            }
        }
        if (toAdd.Length > 0)
        {
            var add = await users.AddToRolesAsync(user, toAdd).ConfigureAwait(false);
            if (!add.Succeeded)
            {
                return TypedResults.ValidationProblem(ToProblemDict(add));
            }
        }

        LogUserUpdated(logger, user.Email ?? string.Empty, user.Id);
        var freshRoles = (IReadOnlyList<string>)await users.GetRolesAsync(user).ConfigureAwait(false);
        return TypedResults.Ok(new AdminUserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.IsDisabled,
            user.IsOidcProvisioned,
            freshRoles,
            user.CreatedUtc,
            user.LastLoginUtc));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPasswordAsync(
        int id,
        [FromBody] ResetPasswordRequest request,
        UserManager<NiewebUser> users,
        ILogger<AdminUsersMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.Users
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        // Admin-initiated reset: skip the email-token round-trip
        // (GeneratePasswordResetTokenAsync needs a TokenProvider that
        // AddIdentityCore does not register by default) and swap the
        // password hash directly. Password validators (length, digits,
        // uppercase, unique-chars, ...) still run inside AddPasswordAsync,
        // so weak passwords are still refused.
        if (await users.HasPasswordAsync(user).ConfigureAwait(false))
        {
            var remove = await users.RemovePasswordAsync(user).ConfigureAwait(false);
            if (!remove.Succeeded)
            {
                return TypedResults.ValidationProblem(ToProblemDict(remove));
            }
        }
        var add = await users.AddPasswordAsync(user, request.NewPassword).ConfigureAwait(false);
        if (!add.Succeeded)
        {
            return TypedResults.ValidationProblem(ToProblemDict(add));
        }

        // Admin-initiated resets always require the user to pick a
        // fresh password on their next sign-in — the operator's
        // temporary value should not live on after that first login.
        user.MustRotatePassword = true;
        user.LastModifiedUtc = DateTime.UtcNow;
        _ = await users.UpdateAsync(user).ConfigureAwait(false);

        LogPasswordReset(logger, user.Email ?? string.Empty, user.Id);
        return TypedResults.NoContent();
    }

    private static string? ValidateRoles(IReadOnlyList<string> roles)
    {
        var allowed = new[] { BootstrapAdmin.RoleReader, BootstrapAdmin.RoleAuthor, BootstrapAdmin.RoleAdmin };
        foreach (var r in roles)
        {
            if (!allowed.Contains(r, StringComparer.OrdinalIgnoreCase))
            {
                return $"Unknown role '{r}'. Allowed: {string.Join(", ", allowed)}.";
            }
        }
        // Reject duplicates so 'Admin, Admin' can't sneak past Identity's
        // idempotent AddToRoles path with an ambiguous audit trail.
        if (roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != roles.Count)
        {
            return "Duplicate roles in request.";
        }
        return null;
    }

    private static Dictionary<string, string[]> ToProblemDict(IdentityResult result)
    {
        return result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Admin created user {Email} (userId={UserId})")]
    private static partial void LogUserCreated(ILogger logger, string email, int userId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Admin updated user {Email} (userId={UserId})")]
    private static partial void LogUserUpdated(ILogger logger, string email, int userId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Admin reset password for user {Email} (userId={UserId})")]
    private static partial void LogPasswordReset(ILogger logger, string email, int userId);
}
