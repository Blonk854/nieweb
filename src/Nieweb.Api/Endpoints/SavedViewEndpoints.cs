using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for user-owned "saved views" (a named
/// filter + layout snapshot for one report). Backed by the
/// <see cref="Nieweb.Data.NiewebDbContext.SavedViews"/> table.
///
/// Ownership rules:
///  - A view has a single owning user. Only that user can rename,
///    edit the filter, toggle IsShared, or delete it.
///  - When a view has <c>IsShared = true</c>, every authenticated user
///    can list and apply it, but only the owner can mutate it.
///  - <c>GET /api/saved-views?reportKey=...</c> returns the user's own
///    views for that report plus every shared view for that report,
///    de-duplicated and stable-sorted by name (case-insensitive).
///
/// The filter payload is stored as opaque JSON (<see cref="SavedView.FilterJson"/>)
/// so the API stays agnostic of any single report's filter shape.
/// We do validate that the string parses as JSON before persisting -
/// that way we never let malformed payloads leak back into the client.
/// </summary>
public static partial class SavedViewEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class SavedViewsMarker;

    /// <summary>
    /// Registers the <c>/api/saved-views</c> endpoints on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapSavedViewEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/saved-views")
            .WithTags("SavedViews")
            .RequireAuthorization();

        group.MapGet(string.Empty, ListAsync).WithName("SavedViewsList");
        group.MapPost(string.Empty, CreateAsync).WithName("SavedViewsCreate");
        group.MapPut("/{id:int}", UpdateAsync).WithName("SavedViewsUpdate");
        group.MapDelete("/{id:int}", DeleteAsync).WithName("SavedViewsDelete");

        return routes;
    }

    /// <summary>Response DTO. Never leaks concurrency token or owner PK to non-owners.</summary>
    public sealed record SavedViewDto(
        int Id,
        string Name,
        string ReportKey,
        string FilterJson,
        bool IsShared,
        bool IsOwner,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>Create payload.</summary>
    public sealed record CreateSavedViewRequest
    {
        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; init; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 1)]
        public string ReportKey { get; init; } = string.Empty;

        [Required]
        public string FilterJson { get; init; } = "{}";

        public bool IsShared { get; init; }
    }

    /// <summary>Update payload. All three fields are replaced atomically.</summary>
    public sealed record UpdateSavedViewRequest
    {
        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; init; } = string.Empty;

        [Required]
        public string FilterJson { get; init; } = "{}";

        public bool IsShared { get; init; }
    }

    // -----------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------

    private static async Task<Results<Ok<SavedViewDto[]>, UnauthorizedHttpResult>> ListAsync(
        [FromQuery] string? reportKey,
        ClaimsPrincipal principal,
        NiewebDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var query = db.SavedViews.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(reportKey))
        {
            query = query.Where(v => v.ReportKey == reportKey);
        }

        // Union of "mine" (any share flag) and "shared by others".
        var rows = await query
            .Where(v => v.OwnerUserId == userId || v.IsShared)
            .OrderBy(v => v.Name)
            .ThenBy(v => v.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = rows.Select(v => ToDto(v, userId)).ToArray();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Created<SavedViewDto>, ValidationProblem, UnauthorizedHttpResult>> CreateAsync(
        [FromBody] CreateSavedViewRequest body,
        ClaimsPrincipal principal,
        NiewebDbContext db,
        ILogger<SavedViewsMarker> logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TryValidateJson(body.FilterJson, out var jsonError))
        {
            return ValidationProblem("FilterJson", jsonError);
        }

        var now = DateTime.UtcNow;
        var entity = new SavedView
        {
            OwnerUserId = userId,
            Name = body.Name.Trim(),
            ReportKey = body.ReportKey.Trim(),
            FilterJson = body.FilterJson,
            IsShared = body.IsShared,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };

        db.SavedViews.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        LogCreated(logger, entity.Id, userId, entity.ReportKey);
        var dto = ToDto(entity, userId);
        return TypedResults.Created($"/api/saved-views/{entity.Id}", dto);
    }

    private static async Task<Results<Ok<SavedViewDto>, NotFound, ForbidHttpResult, ValidationProblem, UnauthorizedHttpResult>> UpdateAsync(
        int id,
        [FromBody] UpdateSavedViewRequest body,
        ClaimsPrincipal principal,
        NiewebDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TryValidateJson(body.FilterJson, out var jsonError))
        {
            return ValidationProblem("FilterJson", jsonError);
        }

        var entity = await db.SavedViews.FirstOrDefaultAsync(v => v.Id == id, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return TypedResults.NotFound();
        }
        if (entity.OwnerUserId != userId)
        {
            // Non-owners cannot edit even shared views.
            return TypedResults.Forbid();
        }

        entity.Name = body.Name.Trim();
        entity.FilterJson = body.FilterJson;
        entity.IsShared = body.IsShared;
        entity.LastModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToDto(entity, userId));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        int id,
        ClaimsPrincipal principal,
        NiewebDbContext db,
        ILogger<SavedViewsMarker> logger,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var entity = await db.SavedViews.FirstOrDefaultAsync(v => v.Id == id, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return TypedResults.NotFound();
        }
        if (entity.OwnerUserId != userId)
        {
            return TypedResults.Forbid();
        }

        db.SavedViews.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        LogDeleted(logger, entity.Id, userId);
        return TypedResults.NoContent();
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static SavedViewDto ToDto(SavedView v, int currentUserId) => new(
        Id: v.Id,
        Name: v.Name,
        ReportKey: v.ReportKey,
        FilterJson: v.FilterJson,
        IsShared: v.IsShared,
        IsOwner: v.OwnerUserId == currentUserId,
        CreatedUtc: v.CreatedUtc,
        LastModifiedUtc: v.LastModifiedUtc);

    private static bool TryGetUserId(ClaimsPrincipal principal, out int userId)
    {
        userId = 0;
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out userId);
    }

    private static bool TryValidateJson(string s, out string error)
    {
        try
        {
            using var _ = JsonDocument.Parse(s);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ValidationProblem ValidationProblem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = new[] { message },
        });

    // -----------------------------------------------------------------
    // Logging
    // -----------------------------------------------------------------

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information,
        Message = "SavedView created (id={SavedViewId}, ownerUserId={UserId}, reportKey={ReportKey})")]
    private static partial void LogCreated(ILogger logger, int savedViewId, int userId, string reportKey);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Information,
        Message = "SavedView deleted (id={SavedViewId}, ownerUserId={UserId})")]
    private static partial void LogDeleted(ILogger logger, int savedViewId, int userId);
}
