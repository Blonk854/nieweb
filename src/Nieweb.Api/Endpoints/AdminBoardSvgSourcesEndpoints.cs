using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.BoardSvgs;
using Nieweb.Api.Startup;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only CRUD for <see cref="Nieweb.Data.Entities.BoardSvgSource"/>
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase A). Each source represents
/// one AOI machine's SVG output directory; the sync worker added in
/// Phase B iterates the enabled rows and pulls the newest matching
/// file per product into the local cache.
/// </summary>
/// <remarks>
/// <para>
/// Routes are gated by the <c>Admin</c> role and backed by
/// <see cref="IBoardSvgSources"/>. Every write emits an audit row
/// (<see cref="AuditEventTypes.BoardSvgSourceAdded"/>,
/// <see cref="AuditEventTypes.BoardSvgSourceUpdated"/>,
/// <see cref="AuditEventTypes.BoardSvgSourceRemoved"/>) with
/// before / after snapshots so admins can reconstruct changes.
/// </para>
/// <para>
/// Duplicate machine names raise HTTP 409 with a human-readable
/// message. Path values are stored verbatim (no normalisation) so
/// that operators can tell exactly what they typed even when a
/// share is temporarily unreachable.
/// </para>
/// </remarks>
public static partial class AdminBoardSvgSourcesEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminBoardSvgSourcesMarker;

    private static readonly string[] MachineNameEmptyErrors = new[] { "MachineName must not be empty." };
    private static readonly string[] UncPathEmptyErrors = new[] { "UncPath must not be empty." };

    /// <summary>
    /// Registers the <c>/api/admin/board-svgs/sources</c> endpoints
    /// on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminBoardSvgSourcesEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/board-svgs/sources")
            .WithTags("AdminBoardSvgSources")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminBoardSvgSourcesList");
        group.MapGet("/{id:int}", GetAsync).WithName("AdminBoardSvgSourcesGet");
        group.MapPost(string.Empty, CreateAsync).WithName("AdminBoardSvgSourcesCreate");
        group.MapPut("/{id:int}", UpdateAsync).WithName("AdminBoardSvgSourcesUpdate");
        group.MapDelete("/{id:int}", DeleteAsync).WithName("AdminBoardSvgSourcesDelete");

        return routes;
    }

    /// <summary>Row DTO returned by list / get / create / update.</summary>
    public sealed record BoardSvgSourceDto(
        int Id,
        string MachineName,
        string UncPath,
        bool IsEnabled,
        DateTime? LastSyncedUtc,
        DateTime? LastSyncErrorUtc,
        string? LastSyncError,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>POST payload for creating a source.</summary>
    public sealed record CreateSourceRequest
    {
        /// <summary>Machine display name (unique).</summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string MachineName { get; init; } = string.Empty;

        /// <summary>UNC path or local absolute path.</summary>
        [Required, StringLength(1024, MinimumLength = 1)]
        public string UncPath { get; init; } = string.Empty;

        /// <summary>When <c>false</c>, sync worker skips this source.</summary>
        public bool IsEnabled { get; init; } = true;
    }

    /// <summary>PUT payload for updating a source.</summary>
    public sealed record UpdateSourceRequest
    {
        /// <summary>Machine display name (unique).</summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string MachineName { get; init; } = string.Empty;

        /// <summary>UNC path or local absolute path.</summary>
        [Required, StringLength(1024, MinimumLength = 1)]
        public string UncPath { get; init; } = string.Empty;

        /// <summary>When <c>false</c>, sync worker skips this source.</summary>
        public bool IsEnabled { get; init; } = true;
    }

    private static async Task<Ok<IReadOnlyList<BoardSvgSourceDto>>> ListAsync(
        IBoardSvgSources sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var rows = await sources.ListAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(ToDto).ToList();
        return TypedResults.Ok((IReadOnlyList<BoardSvgSourceDto>)dtos);
    }

    private static async Task<Results<Ok<BoardSvgSourceDto>, NotFound>> GetAsync(
        int id,
        IBoardSvgSources sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var row = await sources.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return row is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<Created<BoardSvgSourceDto>, ValidationProblem, Conflict<string>>> CreateAsync(
        [FromBody] CreateSourceRequest request,
        IBoardSvgSources sources,
        IAuditLog audit,
        ILogger<AdminBoardSvgSourcesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        var validation = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.MachineName))
        {
            validation["MachineName"] = MachineNameEmptyErrors;
        }
        if (string.IsNullOrWhiteSpace(request.UncPath))
        {
            validation["UncPath"] = UncPathEmptyErrors;
        }
        if (validation.Count > 0)
        {
            return TypedResults.ValidationProblem(validation);
        }

        BoardSvgSourceRow row;
        try
        {
            row = await sources
                .CreateAsync(request.MachineName, request.UncPath, request.IsEnabled, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BoardSvgSourceConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        LogSourceCreated(logger, row.Id, row.MachineName);
        await audit.WriteAsync(
            AuditEventTypes.BoardSvgSourceAdded,
            AuditTargetTypes.BoardSvgSource,
            row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                machineName = row.MachineName,
                uncPath = row.UncPath,
                isEnabled = row.IsEnabled,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/admin/board-svgs/sources/{row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            ToDto(row));
    }

    private static async Task<Results<Ok<BoardSvgSourceDto>, NotFound, ValidationProblem, Conflict<string>>> UpdateAsync(
        int id,
        [FromBody] UpdateSourceRequest request,
        IBoardSvgSources sources,
        IAuditLog audit,
        ILogger<AdminBoardSvgSourcesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        var validation = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.MachineName))
        {
            validation["MachineName"] = MachineNameEmptyErrors;
        }
        if (string.IsNullOrWhiteSpace(request.UncPath))
        {
            validation["UncPath"] = UncPathEmptyErrors;
        }
        if (validation.Count > 0)
        {
            return TypedResults.ValidationProblem(validation);
        }

        var before = await sources.GetAsync(id, cancellationToken).ConfigureAwait(false);

        BoardSvgSourceRow? row;
        try
        {
            row = await sources
                .UpdateAsync(id, request.MachineName, request.UncPath, request.IsEnabled, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BoardSvgSourceConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        LogSourceUpdated(logger, row.Id, row.MachineName);
        await audit.WriteAsync(
            AuditEventTypes.BoardSvgSourceUpdated,
            AuditTargetTypes.BoardSvgSource,
            row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                before = before is null
                    ? null
                    : new
                    {
                        machineName = before.MachineName,
                        uncPath = before.UncPath,
                        isEnabled = before.IsEnabled,
                    },
                after = new
                {
                    machineName = row.MachineName,
                    uncPath = row.UncPath,
                    isEnabled = row.IsEnabled,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        int id,
        IBoardSvgSources sources,
        IAuditLog audit,
        ILogger<AdminBoardSvgSourcesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(audit);

        var before = await sources.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return TypedResults.NotFound();
        }

        var removed = await sources.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return TypedResults.NotFound();
        }

        LogSourceDeleted(logger, id, before.MachineName);
        await audit.WriteAsync(
            AuditEventTypes.BoardSvgSourceRemoved,
            AuditTargetTypes.BoardSvgSource,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                machineName = before.MachineName,
                uncPath = before.UncPath,
                isEnabled = before.IsEnabled,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static BoardSvgSourceDto ToDto(BoardSvgSourceRow r) => new(
        Id: r.Id,
        MachineName: r.MachineName,
        UncPath: r.UncPath,
        IsEnabled: r.IsEnabled,
        LastSyncedUtc: r.LastSyncedUtc,
        LastSyncErrorUtc: r.LastSyncErrorUtc,
        LastSyncError: r.LastSyncError,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    [LoggerMessage(EventId = 3501, Level = LogLevel.Information,
        Message = "Admin created board-SVG source {SourceId} ({MachineName})")]
    private static partial void LogSourceCreated(ILogger logger, int sourceId, string machineName);

    [LoggerMessage(EventId = 3502, Level = LogLevel.Information,
        Message = "Admin updated board-SVG source {SourceId} ({MachineName})")]
    private static partial void LogSourceUpdated(ILogger logger, int sourceId, string machineName);

    [LoggerMessage(EventId = 3503, Level = LogLevel.Information,
        Message = "Admin deleted board-SVG source {SourceId} ({MachineName})")]
    private static partial void LogSourceDeleted(ILogger logger, int sourceId, string machineName);
}
