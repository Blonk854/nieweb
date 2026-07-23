using System.ComponentModel.DataAnnotations;
using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.Reports;
using Nieweb.Api.Startup;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only CRUD for report composition (docs/phase-2.md §7.6
/// <c>RC1</c>). Groups live at
/// <c>/api/admin/report-groups</c>, reports at
/// <c>/api/admin/reports</c>, and per-report tiles at
/// <c>/api/admin/reports/{id}/entities</c>. RC2's SPA editor will
/// consume these endpoints; user-owned (non-admin) creation is
/// intentionally deferred to RC2 so the ownership / locking policy
/// can be validated separately.
/// </summary>
/// <remarks>
/// Every write emits an audit row with a before / after payload so
/// admins can trace who moved which tile where. LoggerMessage event
/// ids: 3301-3309 (groups + reports + entities), leaving 32xx and
/// 33xx blocks open for future admin surfaces.
/// </remarks>
public static partial class AdminReportsEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminReportsMarker;

    private static readonly string[] TitleEmptyErrors = new[] { "Title must not be empty." };
    private static readonly string[] TileTypeEmptyErrors = new[] { "TileType must not be empty." };
    private static readonly string[] OwnerEmptyErrors = new[] { "OwnerDisplayName must not be empty." };
    private static readonly string[] NameEmptyErrors = new[] { "Name must not be empty." };

    /// <summary>
    /// Registers the report-composition admin endpoints on
    /// <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminReportsEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var groups = routes.MapGroup("/api/admin/report-groups")
            .WithTags("AdminReportGroups")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        groups.MapGet(string.Empty, ListGroupsAsync).WithName("AdminReportGroupsList");
        groups.MapPost(string.Empty, CreateGroupAsync).WithName("AdminReportGroupsCreate");
        groups.MapPut("/{id:int}", UpdateGroupAsync).WithName("AdminReportGroupsUpdate");
        groups.MapDelete("/{id:int}", DeleteGroupAsync).WithName("AdminReportGroupsDelete");

        var reports = routes.MapGroup("/api/admin/reports")
            .WithTags("AdminReports")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        reports.MapGet(string.Empty, ListReportsAsync).WithName("AdminReportsList");
        reports.MapGet("/{id:int}", GetReportAsync).WithName("AdminReportsGet");
        reports.MapPost(string.Empty, CreateReportAsync).WithName("AdminReportsCreate");
        reports.MapPut("/{id:int}", UpdateReportAsync).WithName("AdminReportsUpdate");
        reports.MapDelete("/{id:int}", DeleteReportAsync).WithName("AdminReportsDelete");

        reports.MapPost("/{id:int}/entities", AddEntityAsync).WithName("AdminReportsAddEntity");
        reports.MapPut("/{id:int}/entities/{entityId:int}", UpdateEntityAsync).WithName("AdminReportsUpdateEntity");
        reports.MapDelete("/{id:int}/entities/{entityId:int}", RemoveEntityAsync).WithName("AdminReportsRemoveEntity");

        // RC3: lock / unlock / duplicate live on the same admin scope
        // as RC1's CRUD. Locking is a distinct action from PUT /{id}
        // so the header form can never accidentally clear a lock.
        reports.MapPost("/{id:int}/lock", LockReportAsync).WithName("AdminReportsLock");
        reports.MapPost("/{id:int}/unlock", UnlockReportAsync).WithName("AdminReportsUnlock");
        reports.MapPost("/{id:int}/duplicate", DuplicateReportAsync).WithName("AdminReportsDuplicate");

        // F14: dedicated pin / unpin endpoints so the home page and
        // the reports list can toggle without submitting the full
        // header form via PUT /{id}. Idempotent.
        reports.MapPost("/{id:int}/pin", PinReportAsync).WithName("AdminReportsPin");
        reports.MapPost("/{id:int}/unpin", UnpinReportAsync).WithName("AdminReportsUnpin");

        return routes;
    }

    // -------------------- DTOs --------------------

    /// <summary>Group DTO returned by list / create / update.</summary>
    public sealed record ReportGroupDto(
        int Id,
        string Name,
        int DisplayOrder,
        int ReportCount,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>Report DTO returned by list / create / update.</summary>
    public sealed record ReportDto(
        int Id,
        string Title,
        string? Description,
        int? ReportGroupId,
        string? GroupName,
        int? OwnerUserId,
        string OwnerDisplayName,
        bool IsLocked,
        bool IsPinnedHome,
        int? RefreshFrequencySeconds,
        string? ChromeJson,
        int DisplayOrder,
        int EntityCount,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>Report-entity (tile) DTO.</summary>
    public sealed record ReportEntityDto(
        int Id,
        int ReportId,
        string TileType,
        string? Title,
        int DisplayOrder,
        string ConfigJson,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>Full report detail returned by GET /{id}.</summary>
    public sealed record ReportDetailDto(ReportDto Report, IReadOnlyList<ReportEntityDto> Entities);

    /// <summary>POST payload for creating / renaming a group.</summary>
    public sealed record GroupRequest
    {
        /// <summary>Display name (e.g. <c>"Daily production"</c>).</summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string Name { get; init; } = string.Empty;

        /// <summary>Manual sort key (default 0).</summary>
        public int DisplayOrder { get; init; }
    }

    /// <summary>POST payload for creating a report.</summary>
    public sealed record CreateReportRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string Title { get; init; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; init; }

        public int? ReportGroupId { get; init; }

        /// <summary>
        /// Optional snapshot of the owning user's id. When omitted the
        /// row records only <see cref="OwnerDisplayName"/> — useful for
        /// admin-created "template" reports without a real owner.
        /// </summary>
        public int? OwnerUserId { get; init; }

        [Required, StringLength(200, MinimumLength = 1)]
        public string OwnerDisplayName { get; init; } = string.Empty;

        public bool IsLocked { get; init; }
        public bool IsPinnedHome { get; init; }
        public int? RefreshFrequencySeconds { get; init; }
        public string? ChromeJson { get; init; }
        public int DisplayOrder { get; init; }
    }

    /// <summary>PUT payload for updating a report header.</summary>
    public sealed record UpdateReportRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string Title { get; init; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; init; }

        public int? ReportGroupId { get; init; }
        public bool IsLocked { get; init; }
        public bool IsPinnedHome { get; init; }
        public int? RefreshFrequencySeconds { get; init; }
        public string? ChromeJson { get; init; }
        public int DisplayOrder { get; init; }
    }

    /// <summary>POST / PUT payload for a report tile.</summary>
    public sealed record EntityRequest
    {
        [Required, StringLength(100, MinimumLength = 1)]
        public string TileType { get; init; } = string.Empty;

        [StringLength(200)]
        public string? Title { get; init; }

        /// <summary>
        /// Manual sort key. On <c>POST</c>, a value of <c>-1</c>
        /// appends the tile at the end of the report (max+1).
        /// </summary>
        public int DisplayOrder { get; init; } = -1;

        /// <summary>Opaque tile-specific configuration blob (JSON).</summary>
        public string? ConfigJson { get; init; }
    }

    /// <summary>POST payload for <c>/{id}/lock</c> and <c>/{id}/unlock</c> (RC3).</summary>
    public sealed record ReportPasswordRequest
    {
        /// <summary>
        /// Plain-text lock password. On <c>/lock</c> the server hashes
        /// with Argon2id and stores only the hash; on <c>/unlock</c>
        /// the server re-hashes and verifies in constant time.
        /// </summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string Password { get; init; } = string.Empty;
    }

    /// <summary>POST payload for <c>/{id}/duplicate</c> (RC3).</summary>
    public sealed record DuplicateReportRequest
    {
        /// <summary>
        /// Title of the new duplicate. When omitted the server uses
        /// <c>"Copy of {source title}"</c>.
        /// </summary>
        [StringLength(200)]
        public string? Title { get; init; }

        /// <summary>Optional snapshot of the caller's user id.</summary>
        public int? OwnerUserId { get; init; }

        /// <summary>Display name recorded as the duplicate's owner.</summary>
        [Required, StringLength(200, MinimumLength = 1)]
        public string OwnerDisplayName { get; init; } = string.Empty;
    }

    // -------------------- Groups --------------------

    private static async Task<Ok<IReadOnlyList<ReportGroupDto>>> ListGroupsAsync(
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var rows = await reports.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(ToGroupDto).ToList();
        return TypedResults.Ok((IReadOnlyList<ReportGroupDto>)dtos);
    }

    private static async Task<Results<Created<ReportGroupDto>, ValidationProblem, Conflict<string>>> CreateGroupAsync(
        [FromBody] GroupRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = NameEmptyErrors,
            });
        }
        ReportGroupRow row;
        try
        {
            row = await reports.CreateGroupAsync(request.Name, request.DisplayOrder, cancellationToken).ConfigureAwait(false);
        }
        catch (ReportConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        LogGroupCreated(logger, row.Id, row.Name);
        await audit.WriteAsync(
            AuditEventTypes.ReportGroupCreated,
            AuditTargetTypes.ReportGroup,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { name = row.Name, displayOrder = row.DisplayOrder },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"/api/admin/report-groups/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToGroupDto(row));
    }

    private static async Task<Results<Ok<ReportGroupDto>, NotFound, ValidationProblem, Conflict<string>>> UpdateGroupAsync(
        int id,
        [FromBody] GroupRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = NameEmptyErrors,
            });
        }
        ReportGroupRow? row;
        try
        {
            row = await reports.UpdateGroupAsync(id, request.Name, request.DisplayOrder, cancellationToken).ConfigureAwait(false);
        }
        catch (ReportConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        if (row is null)
        {
            return TypedResults.NotFound();
        }
        LogGroupUpdated(logger, row.Id, row.Name);
        await audit.WriteAsync(
            AuditEventTypes.ReportGroupUpdated,
            AuditTargetTypes.ReportGroup,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { name = row.Name, displayOrder = row.DisplayOrder },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToGroupDto(row));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGroupAsync(
        int id,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        var removed = await reports.DeleteGroupAsync(id, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return TypedResults.NotFound();
        }
        LogGroupDeleted(logger, id);
        await audit.WriteAsync(
            AuditEventTypes.ReportGroupDeleted,
            AuditTargetTypes.ReportGroup,
            id.ToString(CultureInfo.InvariantCulture),
            new { id },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // -------------------- Reports --------------------

    private static async Task<Ok<IReadOnlyList<ReportDto>>> ListReportsAsync(
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var rows = await reports.ListReportsAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(ToReportDto).ToList();
        return TypedResults.Ok((IReadOnlyList<ReportDto>)dtos);
    }

    private static async Task<Results<Ok<ReportDetailDto>, NotFound>> GetReportAsync(
        int id,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound();
        }
        var entities = detail.Entities.Select(ToEntityDto).ToList();
        return TypedResults.Ok(new ReportDetailDto(ToReportDto(detail.Report), entities));
    }

    private static async Task<Results<Created<ReportDto>, ValidationProblem, Conflict<string>>> CreateReportAsync(
        [FromBody] CreateReportRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        var validation = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            validation["Title"] = TitleEmptyErrors;
        }
        if (string.IsNullOrWhiteSpace(request.OwnerDisplayName))
        {
            validation["OwnerDisplayName"] = OwnerEmptyErrors;
        }
        if (validation.Count > 0)
        {
            return TypedResults.ValidationProblem(validation);
        }

        var input = new CreateReportInput(
            Title: request.Title,
            Description: request.Description,
            ReportGroupId: request.ReportGroupId,
            OwnerUserId: request.OwnerUserId,
            OwnerDisplayName: request.OwnerDisplayName,
            IsLocked: request.IsLocked,
            IsPinnedHome: request.IsPinnedHome,
            RefreshFrequencySeconds: request.RefreshFrequencySeconds,
            ChromeJson: request.ChromeJson,
            DisplayOrder: request.DisplayOrder);

        ReportRow row;
        try
        {
            row = await reports.CreateReportAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (ReportConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "RefreshFrequencySeconds"] = new[] { ex.Message },
            });
        }

        LogReportCreated(logger, row.Id, row.Title);
        await audit.WriteAsync(
            AuditEventTypes.ReportCreated,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new
            {
                title = row.Title,
                reportGroupId = row.ReportGroupId,
                ownerUserId = row.OwnerUserId,
                ownerDisplayName = row.OwnerDisplayName,
                isLocked = row.IsLocked,
                isPinnedHome = row.IsPinnedHome,
                displayOrder = row.DisplayOrder,
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"/api/admin/reports/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToReportDto(row));
    }

    private static async Task<Results<Ok<ReportDto>, NotFound, ValidationProblem, Conflict<string>>> UpdateReportAsync(
        int id,
        [FromBody] UpdateReportRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Title"] = TitleEmptyErrors,
            });
        }
        var input = new UpdateReportInput(
            Title: request.Title,
            Description: request.Description,
            ReportGroupId: request.ReportGroupId,
            IsLocked: request.IsLocked,
            IsPinnedHome: request.IsPinnedHome,
            RefreshFrequencySeconds: request.RefreshFrequencySeconds,
            ChromeJson: request.ChromeJson,
            DisplayOrder: request.DisplayOrder);

        ReportRow? row;
        try
        {
            row = await reports.UpdateReportAsync(id, input, cancellationToken).ConfigureAwait(false);
        }
        catch (ReportConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "RefreshFrequencySeconds"] = new[] { ex.Message },
            });
        }
        if (row is null)
        {
            return TypedResults.NotFound();
        }
        LogReportUpdated(logger, row.Id, row.Title);
        await audit.WriteAsync(
            AuditEventTypes.ReportUpdated,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new
            {
                title = row.Title,
                reportGroupId = row.ReportGroupId,
                isLocked = row.IsLocked,
                isPinnedHome = row.IsPinnedHome,
                displayOrder = row.DisplayOrder,
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToReportDto(row));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteReportAsync(
        int id,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        var removed = await reports.DeleteReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return TypedResults.NotFound();
        }
        LogReportDeleted(logger, id);
        await audit.WriteAsync(
            AuditEventTypes.ReportDeleted,
            AuditTargetTypes.Report,
            id.ToString(CultureInfo.InvariantCulture),
            new { id },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // -------------------- Entities (tiles) --------------------

    private static async Task<Results<Created<ReportEntityDto>, NotFound, ValidationProblem>> AddEntityAsync(
        int id,
        [FromBody] EntityRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TileType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["TileType"] = TileTypeEmptyErrors,
            });
        }
        var input = new AddEntityInput(
            TileType: request.TileType,
            Title: request.Title,
            DisplayOrder: request.DisplayOrder,
            ConfigJson: request.ConfigJson ?? "{}");
        var row = await reports.AddEntityAsync(id, input, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return TypedResults.NotFound();
        }
        LogEntityAdded(logger, row.Id, id, row.TileType);
        await audit.WriteAsync(
            AuditEventTypes.ReportEntityAdded,
            AuditTargetTypes.ReportEntity,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new
            {
                reportId = row.ReportId,
                tileType = row.TileType,
                title = row.Title,
                displayOrder = row.DisplayOrder,
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"/api/admin/reports/{id.ToString(CultureInfo.InvariantCulture)}/entities/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToEntityDto(row));
    }

    private static async Task<Results<Ok<ReportEntityDto>, NotFound, ValidationProblem>> UpdateEntityAsync(
        int id,
        int entityId,
        [FromBody] EntityRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TileType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["TileType"] = TileTypeEmptyErrors,
            });
        }
        var input = new UpdateEntityInput(
            TileType: request.TileType,
            Title: request.Title,
            DisplayOrder: request.DisplayOrder,
            ConfigJson: request.ConfigJson ?? "{}");
        var row = await reports.UpdateEntityAsync(id, entityId, input, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return TypedResults.NotFound();
        }
        LogEntityUpdated(logger, row.Id, id, row.TileType);
        await audit.WriteAsync(
            AuditEventTypes.ReportEntityUpdated,
            AuditTargetTypes.ReportEntity,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new
            {
                reportId = row.ReportId,
                tileType = row.TileType,
                title = row.Title,
                displayOrder = row.DisplayOrder,
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToEntityDto(row));
    }

    private static async Task<Results<NoContent, NotFound>> RemoveEntityAsync(
        int id,
        int entityId,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        var removed = await reports.RemoveEntityAsync(id, entityId, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return TypedResults.NotFound();
        }
        LogEntityRemoved(logger, entityId, id);
        await audit.WriteAsync(
            AuditEventTypes.ReportEntityRemoved,
            AuditTargetTypes.ReportEntity,
            entityId.ToString(CultureInfo.InvariantCulture),
            new { reportId = id, entityId },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // -------------------- Lock / unlock / duplicate (RC3) --------------------

    private static readonly string[] PasswordEmptyErrors = new[] { "Password must not be empty." };

    private static async Task<Results<Ok<ReportDto>, NotFound, ValidationProblem>> LockReportAsync(
        int id,
        [FromBody] ReportPasswordRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Password"] = PasswordEmptyErrors,
            });
        }
        var outcome = await reports.LockReportAsync(id, request.Password, cancellationToken).ConfigureAwait(false);
        switch (outcome.Result)
        {
            case LockResult.NotFound:
                return TypedResults.NotFound();
            case LockResult.PasswordEmpty:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Password"] = PasswordEmptyErrors,
                });
            case LockResult.Success:
                var row = outcome.Report!;
                LogReportLocked(logger, row.Id);
                await audit.WriteAsync(
                    AuditEventTypes.ReportLocked,
                    AuditTargetTypes.Report,
                    row.Id.ToString(CultureInfo.InvariantCulture),
                    new { title = row.Title },
                    cancellationToken).ConfigureAwait(false);
                return TypedResults.Ok(ToReportDto(row));
            default:
                throw new InvalidOperationException($"Unexpected lock result: {outcome.Result}.");
        }
    }

    private static async Task<Results<Ok<ReportDto>, NotFound, ValidationProblem, UnauthorizedHttpResult, Conflict<string>>> UnlockReportAsync(
        int id,
        [FromBody] ReportPasswordRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Password"] = PasswordEmptyErrors,
            });
        }
        var outcome = await reports.UnlockReportAsync(id, request.Password, cancellationToken).ConfigureAwait(false);
        switch (outcome.Result)
        {
            case UnlockResult.NotFound:
                return TypedResults.NotFound();
            case UnlockResult.NotLocked:
                return TypedResults.Conflict("Report is not locked.");
            case UnlockResult.WrongPassword:
                LogReportUnlockFailed(logger, id);
                return TypedResults.Unauthorized();
            case UnlockResult.Success:
                var row = outcome.Report!;
                LogReportUnlocked(logger, row.Id);
                await audit.WriteAsync(
                    AuditEventTypes.ReportUnlocked,
                    AuditTargetTypes.Report,
                    row.Id.ToString(CultureInfo.InvariantCulture),
                    new { title = row.Title },
                    cancellationToken).ConfigureAwait(false);
                return TypedResults.Ok(ToReportDto(row));
            default:
                throw new InvalidOperationException($"Unexpected unlock result: {outcome.Result}.");
        }
    }

    private static async Task<Results<Created<ReportDto>, NotFound, ValidationProblem>> DuplicateReportAsync(
        int id,
        [FromBody] DuplicateReportRequest request,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OwnerDisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["OwnerDisplayName"] = OwnerEmptyErrors,
            });
        }

        // If the caller didn't supply a title we need to look up the
        // source so we can prefix "Copy of ". A missing source id is
        // reported the same way whether or not a title was given.
        string title;
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            title = request.Title.Trim();
        }
        else
        {
            var source = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return TypedResults.NotFound();
            }
            title = $"Copy of {source.Report.Title}";
            if (title.Length > 200)
            {
                title = title[..200];
            }
        }

        var input = new DuplicateReportInput(
            Title: title,
            OwnerUserId: request.OwnerUserId,
            OwnerDisplayName: request.OwnerDisplayName);
        var row = await reports.DuplicateReportAsync(id, input, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return TypedResults.NotFound();
        }
        LogReportDuplicated(logger, id, row.Id);
        await audit.WriteAsync(
            AuditEventTypes.ReportDuplicated,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { sourceId = id, title = row.Title },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"/api/admin/reports/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToReportDto(row));
    }

    // -------------------- Pin / unpin (F14) --------------------

    private static Task<Results<Ok<ReportDto>, NotFound>> PinReportAsync(
        int id,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken) =>
        SetPinnedHomeAsync(id, pinned: true, reports, audit, logger, cancellationToken);

    private static Task<Results<Ok<ReportDto>, NotFound>> UnpinReportAsync(
        int id,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken) =>
        SetPinnedHomeAsync(id, pinned: false, reports, audit, logger, cancellationToken);

    private static async Task<Results<Ok<ReportDto>, NotFound>> SetPinnedHomeAsync(
        int id,
        bool pinned,
        IReports reports,
        IAuditLog audit,
        ILogger<AdminReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);

        var row = await reports.SetPinnedHomeAsync(id, pinned, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return TypedResults.NotFound();
        }
        if (pinned)
        {
            LogReportPinned(logger, row.Id);
        }
        else
        {
            LogReportUnpinned(logger, row.Id);
        }
        await audit.WriteAsync(
            pinned ? AuditEventTypes.ReportPinned : AuditEventTypes.ReportUnpinned,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { title = row.Title },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToReportDto(row));
    }

    // -------------------- Mappers --------------------

    private static ReportGroupDto ToGroupDto(ReportGroupRow r) => new(
        Id: r.Id,
        Name: r.Name,
        DisplayOrder: r.DisplayOrder,
        ReportCount: r.ReportCount,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    private static ReportDto ToReportDto(ReportRow r) => new(
        Id: r.Id,
        Title: r.Title,
        Description: r.Description,
        ReportGroupId: r.ReportGroupId,
        GroupName: r.GroupName,
        OwnerUserId: r.OwnerUserId,
        OwnerDisplayName: r.OwnerDisplayName,
        IsLocked: r.IsLocked,
        IsPinnedHome: r.IsPinnedHome,
        RefreshFrequencySeconds: r.RefreshFrequencySeconds,
        ChromeJson: r.ChromeJson,
        DisplayOrder: r.DisplayOrder,
        EntityCount: r.EntityCount,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    private static ReportEntityDto ToEntityDto(ReportEntityRow r) => new(
        Id: r.Id,
        ReportId: r.ReportId,
        TileType: r.TileType,
        Title: r.Title,
        DisplayOrder: r.DisplayOrder,
        ConfigJson: r.ConfigJson,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    // -------------------- Logging --------------------

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information,
        Message = "Admin created report group {GroupId} ({GroupName})")]
    private static partial void LogGroupCreated(ILogger logger, int groupId, string groupName);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information,
        Message = "Admin updated report group {GroupId} ({GroupName})")]
    private static partial void LogGroupUpdated(ILogger logger, int groupId, string groupName);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Information,
        Message = "Admin deleted report group {GroupId}")]
    private static partial void LogGroupDeleted(ILogger logger, int groupId);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Information,
        Message = "Admin created report {ReportId} ({ReportTitle})")]
    private static partial void LogReportCreated(ILogger logger, int reportId, string reportTitle);

    [LoggerMessage(EventId = 3305, Level = LogLevel.Information,
        Message = "Admin updated report {ReportId} ({ReportTitle})")]
    private static partial void LogReportUpdated(ILogger logger, int reportId, string reportTitle);

    [LoggerMessage(EventId = 3306, Level = LogLevel.Information,
        Message = "Admin deleted report {ReportId}")]
    private static partial void LogReportDeleted(ILogger logger, int reportId);

    [LoggerMessage(EventId = 3307, Level = LogLevel.Information,
        Message = "Admin added tile {EntityId} ({TileType}) to report {ReportId}")]
    private static partial void LogEntityAdded(ILogger logger, int entityId, int reportId, string tileType);

    [LoggerMessage(EventId = 3308, Level = LogLevel.Information,
        Message = "Admin updated tile {EntityId} ({TileType}) in report {ReportId}")]
    private static partial void LogEntityUpdated(ILogger logger, int entityId, int reportId, string tileType);

    [LoggerMessage(EventId = 3309, Level = LogLevel.Information,
        Message = "Admin removed tile {EntityId} from report {ReportId}")]
    private static partial void LogEntityRemoved(ILogger logger, int entityId, int reportId);

    [LoggerMessage(EventId = 3310, Level = LogLevel.Information,
        Message = "Admin locked report {ReportId}")]
    private static partial void LogReportLocked(ILogger logger, int reportId);

    [LoggerMessage(EventId = 3311, Level = LogLevel.Information,
        Message = "Admin unlocked report {ReportId}")]
    private static partial void LogReportUnlocked(ILogger logger, int reportId);

    [LoggerMessage(EventId = 3312, Level = LogLevel.Warning,
        Message = "Failed unlock attempt on report {ReportId}")]
    private static partial void LogReportUnlockFailed(ILogger logger, int reportId);

    [LoggerMessage(EventId = 3313, Level = LogLevel.Information,
        Message = "Admin duplicated report {SourceId} into {NewId}")]
    private static partial void LogReportDuplicated(ILogger logger, int sourceId, int newId);

    [LoggerMessage(EventId = 3314, Level = LogLevel.Information,
        Message = "Admin pinned report {ReportId} to the home page")]
    private static partial void LogReportPinned(ILogger logger, int reportId);

    [LoggerMessage(EventId = 3315, Level = LogLevel.Information,
        Message = "Admin unpinned report {ReportId} from the home page")]
    private static partial void LogReportUnpinned(ILogger logger, int reportId);
}
