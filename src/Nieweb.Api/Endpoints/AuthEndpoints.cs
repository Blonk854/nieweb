using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Auth;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for authentication:
/// <c>POST /auth/login</c> and <c>GET /auth/whoami</c>.
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

        group.MapGet("/whoami", WhoAmI)
            .RequireAuthorization()
            .WithName("AuthWhoAmI");

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
    public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresUtc);

    /// <summary>Response body for <c>/auth/whoami</c>.</summary>
    /// <param name="UserId">The user's Identity primary key (as string).</param>
    /// <param name="Email">Primary email if present.</param>
    /// <param name="Name">Display name if present, else user name.</param>
    /// <param name="Roles">Roles carried by the current token.</param>
    public sealed record WhoAmIResponse(string? UserId, string? Email, string? Name, IReadOnlyList<string> Roles);

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
        return Results.Ok(new LoginResponse(issued.AccessToken, "Bearer", issued.ExpiresUtc));
    }

    private static IResult WhoAmI(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var response = new WhoAmIResponse(
            UserId: user.FindFirstValue(ClaimTypes.NameIdentifier),
            Email: user.FindFirstValue(ClaimTypes.Email),
            Name: user.Identity?.Name,
            Roles: user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList());
        return Results.Ok(response);
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
}
