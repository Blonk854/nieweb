using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.ProductionLines;
using Nieweb.Api.Startup;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only CRUD for <see cref="Nieweb.Data.Entities.ProductionLine"/>
/// and its <see cref="Nieweb.Data.Entities.ProductionLineMachine"/>
/// assignments (Vieweb §2.4.3 / docs/phase-2.md §7.4 <c>PL1</c>).
/// </summary>
/// <remarks>
/// <para>
/// Routes are gated by the <c>Admin</c> role and backed by
/// <see cref="IProductionLines"/>. Every write emits an audit row
/// (<see cref="AuditEventTypes.ProductionLineCreated"/> etc.) with
/// before / after snapshots.
/// </para>
/// <para>
/// Uniqueness rules: line names are unique; a physical machine
/// (<c>sourceId</c> + Superviseur <c>MACHINE_ID</c>) may be assigned to
/// at most one line at a time. Violations surface as HTTP 409 with a
/// human-readable message the admin UI can display verbatim.
/// </para>
/// </remarks>
public static partial class AdminProductionLinesEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminProductionLinesMarker;

    private static readonly string[] NameEmptyErrors = new[] { "Name must not be empty." };
    private static readonly string[] SourceEmptyErrors = new[] { "SourceId must not be empty." };
    private static readonly string[] MachineNameEmptyErrors = new[] { "MachineName must not be empty." };

    /// <summary>
    /// Registers the <c>/api/admin/production-lines</c> endpoints on
    /// <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminProductionLinesEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/production-lines")
            .WithTags("AdminProductionLines")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminProductionLinesList");
        group.MapGet("/{id:int}", GetAsync).WithName("AdminProductionLinesGet");
        group.MapPost(string.Empty, CreateAsync).WithName("AdminProductionLinesCreate");
        group.MapPut("/{id:int}", UpdateAsync).WithName("AdminProductionLinesUpdate");
        group.MapDelete("/{id:int}", DeleteAsync).WithName("AdminProductionLinesDelete");
        group.MapPost("/{id:int}/machines", AddMachineAsync).WithName("AdminProductionLinesAddMachine");
        group.MapDelete("/{id:int}/machines/{machineAssignmentId:int}", RemoveMachineAsync)
            .WithName("AdminProductionLinesRemoveMachine");

        return routes;
    }

    /// <summary>Row DTO returned by list / create / update.</summary>
    public sealed record ProductionLineDto(
        int Id,
        string Name,
        int DisplayOrder,
        int MachineCount,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>Machine assignment DTO.</summary>
    public sealed record ProductionLineMachineDto(
        int Id,
        int ProductionLineId,
        string SourceId,
        int MachineId,
        string MachineName,
        string? Category,
        int DisplayOrder,
        DateTime CreatedUtc);

    /// <summary>Full detail DTO returned by GET /{id}.</summary>
    public sealed record ProductionLineDetailDto(
        ProductionLineDto Line,
        IReadOnlyList<ProductionLineMachineDto> Machines);

    /// <summary>POST payload for creating a line.</summary>
    public sealed record CreateLineRequest
    {
        /// <summary>Display name (e.g. <c>"Line 1"</c>).</summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string Name { get; init; } = string.Empty;

        /// <summary>Manual sort key (default 0).</summary>
        public int DisplayOrder { get; init; }
    }

    /// <summary>PUT payload for updating a line.</summary>
    public sealed record UpdateLineRequest
    {
        /// <summary>New display name.</summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string Name { get; init; } = string.Empty;

        /// <summary>New sort key.</summary>
        public int DisplayOrder { get; init; }
    }

    /// <summary>POST payload for attaching a machine to a line.</summary>
    public sealed record AddMachineRequest
    {
        /// <summary>Data-source id (<c>"postreflow"</c>, <c>"prereflow"</c>, ...).</summary>
        [Required, StringLength(64, MinimumLength = 1)]
        public string SourceId { get; init; } = string.Empty;

        /// <summary>Superviseur <c>MACHINE.MACHINE_ID</c>.</summary>
        [Required]
        public int MachineId { get; init; }

        /// <summary>
        /// Display label snapshot (<c>MACHINE.NAME</c>). Stored verbatim
        /// so the admin UI stays useful when the source is offline.
        /// </summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string MachineName { get; init; } = string.Empty;

        /// <summary>Optional category (<c>"AOI"</c>, <c>"SPI"</c>, ...).</summary>
        [StringLength(100)]
        public string? Category { get; init; }

        /// <summary>Manual sort key within the line.</summary>
        public int DisplayOrder { get; init; }
    }

    private static async Task<Ok<IReadOnlyList<ProductionLineDto>>> ListAsync(
        IProductionLines lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var rows = await lines.ListAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(ToDto).ToList();
        return TypedResults.Ok((IReadOnlyList<ProductionLineDto>)dtos);
    }

    private static async Task<Results<Ok<ProductionLineDetailDto>, NotFound>> GetAsync(
        int id,
        IProductionLines lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var detail = await lines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound();
        }
        var machines = detail.Machines.Select(ToMachineDto).ToList();
        return TypedResults.Ok(new ProductionLineDetailDto(ToDto(detail.Line), machines));
    }

    private static async Task<Results<Created<ProductionLineDto>, ValidationProblem, Conflict<string>>> CreateAsync(
        [FromBody] CreateLineRequest request,
        IProductionLines lines,
        IAuditLog audit,
        ILogger<AdminProductionLinesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = NameEmptyErrors,
            });
        }

        ProductionLineRow row;
        try
        {
            row = await lines
                .CreateAsync(request.Name, request.DisplayOrder, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProductionLineConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        LogLineCreated(logger, row.Id, row.Name);
        await audit.WriteAsync(
            AuditEventTypes.ProductionLineCreated,
            AuditTargetTypes.ProductionLine,
            row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new { name = row.Name, displayOrder = row.DisplayOrder },
            cancellationToken).ConfigureAwait(false);

        var dto = ToDto(row);
        return TypedResults.Created($"/api/admin/production-lines/{row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}", dto);
    }

    private static async Task<Results<Ok<ProductionLineDto>, NotFound, ValidationProblem, Conflict<string>>> UpdateAsync(
        int id,
        [FromBody] UpdateLineRequest request,
        IProductionLines lines,
        IAuditLog audit,
        ILogger<AdminProductionLinesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = NameEmptyErrors,
            });
        }

        var before = await lines.GetAsync(id, cancellationToken).ConfigureAwait(false);

        ProductionLineRow? row;
        try
        {
            row = await lines
                .UpdateAsync(id, request.Name, request.DisplayOrder, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProductionLineConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        LogLineUpdated(logger, row.Id, row.Name);
        await audit.WriteAsync(
            AuditEventTypes.ProductionLineUpdated,
            AuditTargetTypes.ProductionLine,
            row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                before = before is null
                    ? null
                    : new { name = before.Line.Name, displayOrder = before.Line.DisplayOrder },
                after = new { name = row.Name, displayOrder = row.DisplayOrder },
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        int id,
        IProductionLines lines,
        IAuditLog audit,
        ILogger<AdminProductionLinesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(audit);

        var before = await lines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return TypedResults.NotFound();
        }

        var removed = await lines.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return TypedResults.NotFound();
        }

        LogLineDeleted(logger, id, before.Line.Name);
        await audit.WriteAsync(
            AuditEventTypes.ProductionLineDeleted,
            AuditTargetTypes.ProductionLine,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                name = before.Line.Name,
                displayOrder = before.Line.DisplayOrder,
                machineCount = before.Machines.Count,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<ProductionLineMachineDto>, NotFound, ValidationProblem, Conflict<string>>>
        AddMachineAsync(
            int id,
            [FromBody] AddMachineRequest request,
            IProductionLines lines,
            IAuditLog audit,
            ILogger<AdminProductionLinesMarker> logger,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        var validation = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.SourceId))
        {
            validation["SourceId"] = SourceEmptyErrors;
        }
        if (string.IsNullOrWhiteSpace(request.MachineName))
        {
            validation["MachineName"] = MachineNameEmptyErrors;
        }
        if (validation.Count > 0)
        {
            return TypedResults.ValidationProblem(validation);
        }

        ProductionLineMachineRow? row;
        try
        {
            row = await lines.AddMachineAsync(
                id,
                request.SourceId,
                request.MachineId,
                request.MachineName,
                request.Category,
                request.DisplayOrder,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProductionLineConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        LogMachineAdded(logger, id, row.SourceId, row.MachineId);
        await audit.WriteAsync(
            AuditEventTypes.ProductionLineMachineAdded,
            AuditTargetTypes.ProductionLineMachine,
            row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                productionLineId = row.ProductionLineId,
                sourceId = row.SourceId,
                machineId = row.MachineId,
                machineName = row.MachineName,
                category = row.Category,
                displayOrder = row.DisplayOrder,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/admin/production-lines/{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}/machines/{row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            ToMachineDto(row));
    }

    private static async Task<Results<NoContent, NotFound>> RemoveMachineAsync(
        int id,
        int machineAssignmentId,
        IProductionLines lines,
        IAuditLog audit,
        ILogger<AdminProductionLinesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(audit);

        var before = await lines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var assignment = before?.Machines.FirstOrDefault(m => m.Id == machineAssignmentId);
        if (assignment is null)
        {
            return TypedResults.NotFound();
        }

        var removed = await lines
            .RemoveMachineAsync(id, machineAssignmentId, cancellationToken)
            .ConfigureAwait(false);
        if (!removed)
        {
            return TypedResults.NotFound();
        }

        LogMachineRemoved(logger, id, assignment.SourceId, assignment.MachineId);
        await audit.WriteAsync(
            AuditEventTypes.ProductionLineMachineRemoved,
            AuditTargetTypes.ProductionLineMachine,
            machineAssignmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new
            {
                productionLineId = id,
                sourceId = assignment.SourceId,
                machineId = assignment.MachineId,
                machineName = assignment.MachineName,
                category = assignment.Category,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static ProductionLineDto ToDto(ProductionLineRow r) => new(
        Id: r.Id,
        Name: r.Name,
        DisplayOrder: r.DisplayOrder,
        MachineCount: r.MachineCount,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    private static ProductionLineMachineDto ToMachineDto(ProductionLineMachineRow r) => new(
        Id: r.Id,
        ProductionLineId: r.ProductionLineId,
        SourceId: r.SourceId,
        MachineId: r.MachineId,
        MachineName: r.MachineName,
        Category: r.Category,
        DisplayOrder: r.DisplayOrder,
        CreatedUtc: r.CreatedUtc);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information,
        Message = "Admin created production line {LineId} ({LineName})")]
    private static partial void LogLineCreated(ILogger logger, int lineId, string lineName);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Information,
        Message = "Admin updated production line {LineId} ({LineName})")]
    private static partial void LogLineUpdated(ILogger logger, int lineId, string lineName);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Information,
        Message = "Admin deleted production line {LineId} ({LineName})")]
    private static partial void LogLineDeleted(ILogger logger, int lineId, string lineName);

    [LoggerMessage(EventId = 3204, Level = LogLevel.Information,
        Message = "Admin added machine ({SourceId}, {MachineId}) to production line {LineId}")]
    private static partial void LogMachineAdded(ILogger logger, int lineId, string sourceId, int machineId);

    [LoggerMessage(EventId = 3205, Level = LogLevel.Information,
        Message = "Admin removed machine ({SourceId}, {MachineId}) from production line {LineId}")]
    private static partial void LogMachineRemoved(ILogger logger, int lineId, string sourceId, int machineId);
}
