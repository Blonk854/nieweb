using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Startup;
using Nieweb.Data;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only read-only view over the append-only <c>AuditEvents</c>
/// table. Supports basic filtering + keyset-style pagination so the
/// admin UI can page through months of history without loading the
/// whole set into memory.
/// </summary>
/// <remarks>
/// The endpoint is deliberately narrow: no write operations, no
/// per-row endpoints, no export (yet). Filtering + paging are enough
/// for the MVP. All parameters are optional and default to "most
/// recent 100 rows".
/// </remarks>
public static class AuditEndpoints
{
    /// <summary>Registers the <c>GET /api/admin/audit</c> endpoint.</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/audit")
            .WithTags("AdminAudit")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminAuditList");

        return routes;
    }

    /// <summary>Row DTO returned by the list endpoint.</summary>
    public sealed record AuditEventDto(
        long Id,
        DateTime EventTimeUtc,
        int? ActorUserId,
        string ActorDisplayName,
        string EventType,
        string TargetType,
        string TargetId,
        string DetailsJson,
        string? IpAddress);

    /// <summary>Paged response shape.</summary>
    public sealed record AuditListResponse(
        IReadOnlyList<AuditEventDto> Items,
        int Total,
        int Page,
        int PageSize);

    private static async Task<Ok<AuditListResponse>> ListAsync(
        NiewebDbContext db,
        CancellationToken cancellationToken,
        [FromQuery] string? eventType = null,
        [FromQuery] string? targetType = null,
        [FromQuery] string? targetId = null,
        [FromQuery] int? actorUserId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Clamp pagination to sane bounds. The upper bound protects
        // the admin DB from an accidental "give me the whole log"
        // query; 1 000 rows still fits in a single response payload
        // for even the widest DetailsJson blob we emit.
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            < 1 => 1,
            > 1000 => 1000,
            _ => pageSize,
        };

        var query = db.AuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }
        if (!string.IsNullOrWhiteSpace(targetType))
        {
            query = query.Where(e => e.TargetType == targetType);
        }
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            query = query.Where(e => e.TargetId == targetId);
        }
        if (actorUserId is { } aid)
        {
            query = query.Where(e => e.ActorUserId == aid);
        }
        if (fromUtc is { } from)
        {
            query = query.Where(e => e.EventTimeUtc >= from);
        }
        if (toUtc is { } to)
        {
            query = query.Where(e => e.EventTimeUtc <= to);
        }

        // Count over the filtered set (not the whole table) so the
        // paginator can render "page X of Y" without a second round-
        // trip. Cost is a scan of the filtered subset - the compound
        // indexes on (TargetType, TargetId) + EventType make this
        // cheap for the common admin drill-down.
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(e => e.EventTimeUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEventDto(
                e.Id,
                e.EventTimeUtc,
                e.ActorUserId,
                e.ActorDisplayName,
                e.EventType,
                e.TargetType,
                e.TargetId,
                e.DetailsJson,
                e.IpAddress))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new AuditListResponse(items, total, page, pageSize));
    }
}
