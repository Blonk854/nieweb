using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Auth;
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

        return routes;
    }

    /// <summary>Login request body.</summary>
    public sealed record LoginRequest
    {
        /// <summary>Registered email address (case-insensitive).</summary>
        [Required, EmailAddress]
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
        ILogger<LoginRequestMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindByEmailAsync(request.Email).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            // Do not disclose whether the account exists.
            LogUnknownOrDisabled(logger);
            return Results.Unauthorized();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
            .ConfigureAwait(false);
        if (result.IsLockedOut)
        {
            LogLockedOut(logger, user.Id);
            return Results.Unauthorized();
        }
        if (!result.Succeeded)
        {
            LogBadPassword(logger, user.Id);
            return Results.Unauthorized();
        }

        user.LastLoginUtc = DateTime.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);

        var roles = await users.GetRolesAsync(user).ConfigureAwait(false);
        var issued = tokens.Issue(user, (IReadOnlyCollection<string>)roles);

        LogGranted(logger, user.Id);
        return Results.Ok(new LoginResponse(
            issued.AccessToken,
            "Bearer",
            issued.ExpiresUtc,
            user.MustRotatePassword));
    }

    private static async Task<IResult> WhoAmIAsync(
        ClaimsPrincipal principal,
        UserManager<NiewebUser> users)
    {
        ArgumentNullException.ThrowIfNull(principal);

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
