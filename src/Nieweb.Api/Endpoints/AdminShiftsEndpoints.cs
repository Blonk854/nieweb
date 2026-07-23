using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.Shifts;
using Nieweb.Api.Startup;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only endpoints for the site-wide shift cycle (Vieweb §2.4.4 /
/// docs/phase-2.md §7.4 <c>PL1</c>). The cycle is treated as one
/// atomic unit — <c>GET</c> returns the current breakpoints and
/// <c>PUT</c> replaces the whole list. This matches Vieweb's UX where
/// the admin edits a table then hits "Save".
/// </summary>
/// <remarks>
/// Emits a single <see cref="AuditEventTypes.ShiftsReplaced"/> event on
/// every successful write, with before / after snapshots. Downstream
/// reports (CR1 Pareto, upcoming CR3 Trend, PC1 dashboard) consume the
/// resulting <c>ShiftDefinition</c> via <see cref="IShifts.BuildShiftDefinitionAsync"/>.
/// </remarks>
public static partial class AdminShiftsEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminShiftsMarker;

    /// <summary>
    /// Registers the <c>/api/admin/shifts</c> endpoints on
    /// <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminShiftsEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/shifts")
            .WithTags("AdminShifts")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminShiftsList");
        group.MapPut(string.Empty, ReplaceAsync).WithName("AdminShiftsReplace");

        return routes;
    }

    /// <summary>Row DTO returned by GET / PUT.</summary>
    public sealed record ShiftBreakpointDto(
        int Id,
        int Hour,
        int Minute,
        string? Label,
        int DisplayOrder,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>Individual entry in a <see cref="ReplaceShiftsRequest"/>.</summary>
    public sealed record ShiftBreakpointInputDto
    {
        /// <summary>Hour of day (0–23).</summary>
        [Range(0, 23)]
        public int Hour { get; init; }

        /// <summary>Minute of hour (0–59).</summary>
        [Range(0, 59)]
        public int Minute { get; init; }

        /// <summary>Optional shift label (max 100 chars).</summary>
        [StringLength(100)]
        public string? Label { get; init; }
    }

    /// <summary>PUT payload — the full replacement cycle.</summary>
    public sealed record ReplaceShiftsRequest
    {
        /// <summary>
        /// Ordered list of breakpoints. The server re-sorts by
        /// <c>(Hour, Minute)</c> and rejects duplicates.
        /// </summary>
        [Required]
        public IReadOnlyList<ShiftBreakpointInputDto> Entries { get; init; } = Array.Empty<ShiftBreakpointInputDto>();
    }

    private static async Task<Ok<IReadOnlyList<ShiftBreakpointDto>>> ListAsync(
        IShifts shifts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        var rows = await shifts.ListAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(ToDto).ToList();
        return TypedResults.Ok((IReadOnlyList<ShiftBreakpointDto>)dtos);
    }

    private static async Task<Results<Ok<IReadOnlyList<ShiftBreakpointDto>>, ValidationProblem>> ReplaceAsync(
        [FromBody] ReplaceShiftsRequest request,
        IShifts shifts,
        IAuditLog audit,
        ILogger<AdminShiftsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        var before = await shifts.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ShiftBreakpointRow> rows;
        try
        {
            rows = await shifts.ReplaceAsync(
                (request.Entries ?? Array.Empty<ShiftBreakpointInputDto>())
                    .Select(e => new ShiftBreakpointInput(e.Hour, e.Minute, e.Label)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "Entries"] = new[] { ex.Message },
            });
        }

        var dtos = rows.Select(ToDto).ToList();
        LogShiftsReplaced(logger, rows.Count);
        await audit.WriteAsync(
            AuditEventTypes.ShiftsReplaced,
            AuditTargetTypes.ShiftCycle,
            "site",
            new
            {
                before = before.Select(r => new { r.Hour, r.Minute, r.Label }),
                after = rows.Select(r => new { r.Hour, r.Minute, r.Label }),
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok((IReadOnlyList<ShiftBreakpointDto>)dtos);
    }

    private static ShiftBreakpointDto ToDto(ShiftBreakpointRow r) => new(
        Id: r.Id,
        Hour: r.Hour,
        Minute: r.Minute,
        Label: r.Label,
        DisplayOrder: r.DisplayOrder,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information,
        Message = "Admin replaced shift cycle with {Count} breakpoint(s)")]
    private static partial void LogShiftsReplaced(ILogger logger, int count);
}
