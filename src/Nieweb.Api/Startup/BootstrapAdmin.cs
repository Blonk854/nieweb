using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Startup;

/// <summary>
/// Startup bootstrapper: guarantees the three built-in roles
/// (<c>Reader</c>, <c>Author</c>, <c>Admin</c>) exist and, when the
/// users table is empty, seeds an initial administrator from
/// configuration section <c>Nieweb:Bootstrap:Admin</c>.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by design: role creation checks first, admin creation
/// only fires when the users table is empty. Safe to run on every
/// process start.
/// </para>
/// <para>
/// The bootstrap admin is opt-in - no user is created unless both
/// <c>Nieweb:Bootstrap:Admin:Email</c> and
/// <c>Nieweb:Bootstrap:Admin:Password</c> are non-empty. This keeps
/// production deployments from ever accepting a default password by
/// accident and lets operators disable the bootstrap simply by
/// unsetting those keys after the first admin has been created.
/// </para>
/// </remarks>
public static partial class BootstrapAdmin
{
    /// <summary>Built-in role name for read-only report consumers.</summary>
    public const string RoleReader = "Reader";

    /// <summary>Built-in role name for authors of saved views / reports.</summary>
    public const string RoleAuthor = "Author";

    /// <summary>Built-in role name for full administrators.</summary>
    public const string RoleAdmin = "Admin";

    /// <summary>
    /// Marker type used only to name the <see cref="ILogger{T}"/>
    /// category for the bootstrap - keeps the log category short
    /// and stable.
    /// </summary>
    public sealed class Marker
    {
    }

    /// <summary>
    /// Applies pending EF Core migrations to <see cref="NiewebDbContext"/>,
    /// then ensures roles and (optionally) the bootstrap admin exist.
    /// Call once during startup, immediately before <c>app.Run()</c>.
    /// </summary>
    public static async Task EnsureBootstrapAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<Marker>>();

        var db = sp.GetRequiredService<NiewebDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var roleManager = sp.GetRequiredService<RoleManager<NiewebRole>>();
        foreach (var name in new[] { RoleReader, RoleAuthor, RoleAdmin })
        {
            if (await roleManager.RoleExistsAsync(name).ConfigureAwait(false))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new NiewebRole
            {
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
            }).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create built-in role '{name}': "
                    + string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
            }

            LogRoleCreated(logger, name);
        }

        var users = sp.GetRequiredService<UserManager<NiewebUser>>();
        var anyUser = await users.Users.AnyAsync(cancellationToken).ConfigureAwait(false);
        if (anyUser)
        {
            return;
        }

        var config = sp.GetRequiredService<IConfiguration>();
        var email = config["Nieweb:Bootstrap:Admin:Email"];
        var password = config["Nieweb:Bootstrap:Admin:Password"];
        var displayName = config["Nieweb:Bootstrap:Admin:DisplayName"] ?? "Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            LogBootstrapSkipped(logger);
            return;
        }

        var now = DateTime.UtcNow;
        var admin = new NiewebUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            CreatedUtc = now,
            LastModifiedUtc = now,
            IsOidcProvisioned = false,
        };

        var create = await users.CreateAsync(admin, password).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to create bootstrap admin: "
                + string.Join("; ", create.Errors.Select(e => $"{e.Code}: {e.Description}")));
        }

        var addRole = await users.AddToRoleAsync(admin, RoleAdmin).ConfigureAwait(false);
        if (!addRole.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to assign Admin role to bootstrap admin: "
                + string.Join("; ", addRole.Errors.Select(e => $"{e.Code}: {e.Description}")));
        }

        LogBootstrapCreated(logger, email, admin.Id);
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Created built-in role {Role}")]
    private static partial void LogRoleCreated(ILogger logger, string role);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Users table is empty and Nieweb:Bootstrap:Admin:{{Email,Password}} is not configured. "
            + "No administrator will be created - set those keys before restart, "
            + "or provision the first user via the admin UI once it ships.")]
    private static partial void LogBootstrapSkipped(ILogger logger);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Bootstrap administrator {Email} created (userId={UserId}). Rotate the password now.")]
    private static partial void LogBootstrapCreated(ILogger logger, string email, int userId);
}
