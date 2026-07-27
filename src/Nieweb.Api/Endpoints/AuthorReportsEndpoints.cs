using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.Reports;
using Nieweb.Api.Startup;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Self-service report authoring for non-admin <c>Author</c> users
/// (docs/phase-2.md §7.6 RC2). Mirrors the admin report-composition
/// surface (<see cref="AdminReportsEndpoints"/>) but is mounted at
/// <c>/api/reports</c>, requires the <c>Author</c> (or <c>Admin</c>)
/// role, and scopes every operation to the caller's own reports.
/// </summary>
/// <remarks>
/// <para>
/// Ownership is enforced server-side on every read and write: a report
/// whose <c>OwnerUserId</c> is not the caller returns <c>403</c>, and a
/// missing report returns <c>404</c>. The owner snapshot
/// (<c>OwnerUserId</c> + <c>OwnerDisplayName</c>) is always taken from
/// the authenticated principal — never from the request body — so an
/// author can't create or move a report on someone else's behalf.
/// </para>
/// <para>
/// Two admin-only capabilities are deliberately absent here: pinning a
/// report to the site-wide home page, and editing another user's
/// report. Duplicating <em>any</em> report into a fresh owned copy is
/// allowed (legacy Vieweb "Author" parity — clone a colleague's report
/// and adapt it). Response DTOs are the same shapes the admin surface
/// returns so the SPA client can reuse them.
/// </para>
/// </remarks>
public static partial class AuthorReportsEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AuthorReportsMarker;

    /// <summary>
    /// Registers the <c>/api/reports</c> author endpoints on
    /// <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthorReportsEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/reports")
            .WithTags("AuthorReports")
            .RequireAuthorization(policy =>
                policy.RequireRole(BootstrapAdmin.RoleAuthor, BootstrapAdmin.RoleAdmin));

        group.MapGet("/mine", ListMineAsync).WithName("AuthorReportsListMine");
        group.MapGet("/{id:int}", GetAsync).WithName("AuthorReportsGet");
        group.MapPost(string.Empty, CreateAsync).WithName("AuthorReportsCreate");
        group.MapPut("/{id:int}", UpdateAsync).WithName("AuthorReportsUpdate");
        group.MapDelete("/{id:int}", DeleteAsync).WithName("AuthorReportsDelete");

        group.MapPost("/{id:int}/entities", AddEntityAsync).WithName("AuthorReportsAddEntity");
        group.MapPut("/{id:int}/entities/{entityId:int}", UpdateEntityAsync).WithName("AuthorReportsUpdateEntity");
        group.MapDelete("/{id:int}/entities/{entityId:int}", RemoveEntityAsync).WithName("AuthorReportsRemoveEntity");

        group.MapPost("/{id:int}/lock", LockAsync).WithName("AuthorReportsLock");
        group.MapPost("/{id:int}/unlock", UnlockAsync).WithName("AuthorReportsUnlock");
        group.MapPost("/{id:int}/duplicate", DuplicateAsync).WithName("AuthorReportsDuplicate");

        return routes;
    }

    // -------------------- Request DTOs --------------------

    /// <summary>POST payload for an author creating their own report.</summary>
    public sealed record AuthorCreateReportRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string Title { get; init; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; init; }

        public int? ReportGroupId { get; init; }
        public int? RefreshFrequencySeconds { get; init; }
        public string? ChromeJson { get; init; }
        public int DisplayOrder { get; init; }
    }

    /// <summary>PUT payload for an author updating their own report header.</summary>
    public sealed record AuthorUpdateReportRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string Title { get; init; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; init; }

        public int? ReportGroupId { get; init; }
        public int? RefreshFrequencySeconds { get; init; }
        public string? ChromeJson { get; init; }
        public int DisplayOrder { get; init; }
    }

    /// <summary>POST payload for <c>/{id}/duplicate</c>.</summary>
    public sealed record AuthorDuplicateReportRequest
    {
        /// <summary>
        /// Title of the new copy. When omitted the server uses
        /// <c>"Copy of {source title}"</c>.
        /// </summary>
        [StringLength(200)]
        public string? Title { get; init; }
    }

    private static readonly string[] TitleEmptyErrors = { "Title must not be empty." };
    private static readonly string[] TileTypeEmptyErrors = { "TileType must not be empty." };
    private static readonly string[] PasswordEmptyErrors = { "Password must not be empty." };
    private static readonly string[] PasswordWrongErrors = { "Wrong password or report is not locked." };

    // -------------------- Report handlers --------------------

    private static async Task<Results<Ok<IReadOnlyList<AdminReportsEndpoints.ReportDto>>, UnauthorizedHttpResult>> ListMineAsync(
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var all = await reports.ListReportsAsync(cancellationToken).ConfigureAwait(false);
        var mine = (IReadOnlyList<AdminReportsEndpoints.ReportDto>)all
            .Where(r => r.OwnerUserId == userId)
            .Select(ToReportDto)
            .ToList();
        return TypedResults.Ok(mine);
    }

    private static async Task<Results<Ok<AdminReportsEndpoints.ReportDetailDto>, NotFound, ForbidHttpResult, UnauthorizedHttpResult>> GetAsync(
        int id,
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound();
        }
        if (detail.Report.OwnerUserId != userId)
        {
            return TypedResults.Forbid();
        }

        var entities = detail.Entities.Select(ToEntityDto).ToList();
        return TypedResults.Ok(new AdminReportsEndpoints.ReportDetailDto(ToReportDto(detail.Report), entities));
    }

    private static async Task<Results<Created<AdminReportsEndpoints.ReportDto>, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> CreateAsync(
        [FromBody] AuthorCreateReportRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        IAuditLog audit,
        ILogger<AuthorReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetCaller(principal, out var userId, out var displayName))
        {
            return TypedResults.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Title"] = TitleEmptyErrors,
            });
        }

        var input = new CreateReportInput(
            Title: request.Title,
            Description: request.Description,
            ReportGroupId: request.ReportGroupId,
            OwnerUserId: userId,
            OwnerDisplayName: displayName,
            IsLocked: false,
            IsPinnedHome: false,
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

        LogAuthorReportCreated(logger, row.Id, userId);
        await audit.WriteAsync(
            AuditEventTypes.ReportCreated,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { title = row.Title, ownerUserId = row.OwnerUserId, self = true },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/reports/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToReportDto(row));
    }

    private static async Task<Results<Ok<AdminReportsEndpoints.ReportDto>, NotFound, ForbidHttpResult, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> UpdateAsync(
        int id,
        [FromBody] AuthorUpdateReportRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        IAuditLog audit,
        ILogger<AuthorReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound();
        }
        if (detail.Report.OwnerUserId != userId)
        {
            return TypedResults.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Title"] = TitleEmptyErrors,
            });
        }

        // Preserve lock + pin state: authors change those through the
        // dedicated lock endpoints (pin is admin-only), never via the
        // header PUT, so a stale form can't clear them.
        var input = new UpdateReportInput(
            Title: request.Title,
            Description: request.Description,
            ReportGroupId: request.ReportGroupId,
            IsLocked: detail.Report.IsLocked,
            IsPinnedHome: detail.Report.IsPinnedHome,
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

        await audit.WriteAsync(
            AuditEventTypes.ReportUpdated,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { title = row.Title, self = true },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToReportDto(row));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        int id,
        ClaimsPrincipal principal,
        IReports reports,
        IAuditLog audit,
        ILogger<AuthorReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound();
        }
        if (detail.Report.OwnerUserId != userId)
        {
            return TypedResults.Forbid();
        }

        await reports.DeleteReportAsync(id, cancellationToken).ConfigureAwait(false);
        LogAuthorReportDeleted(logger, id, userId);
        await audit.WriteAsync(
            AuditEventTypes.ReportDeleted,
            AuditTargetTypes.Report,
            id.ToString(CultureInfo.InvariantCulture),
            new { self = true },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // -------------------- Tile handlers --------------------

    private static async Task<Results<Created<AdminReportsEndpoints.ReportEntityDto>, NotFound, ForbidHttpResult, ValidationProblem, UnauthorizedHttpResult>> AddEntityAsync(
        int id,
        [FromBody] AdminReportsEndpoints.EntityRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }
        var owned = await CheckOwnedAsync(reports, id, userId, cancellationToken).ConfigureAwait(false);
        if (owned == Ownership.NotFound)
        {
            return TypedResults.NotFound();
        }
        if (owned == Ownership.Forbidden)
        {
            return TypedResults.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.TileType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["TileType"] = TileTypeEmptyErrors,
            });
        }

        var row = await reports.AddEntityAsync(
            id,
            new AddEntityInput(request.TileType, request.Title, request.DisplayOrder, request.ConfigJson ?? "{}"),
            cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Created(
            $"/api/reports/{id.ToString(CultureInfo.InvariantCulture)}/entities/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToEntityDto(row));
    }

    private static async Task<Results<Ok<AdminReportsEndpoints.ReportEntityDto>, NotFound, ForbidHttpResult, ValidationProblem, UnauthorizedHttpResult>> UpdateEntityAsync(
        int id,
        int entityId,
        [FromBody] AdminReportsEndpoints.EntityRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }
        var owned = await CheckOwnedAsync(reports, id, userId, cancellationToken).ConfigureAwait(false);
        if (owned == Ownership.NotFound)
        {
            return TypedResults.NotFound();
        }
        if (owned == Ownership.Forbidden)
        {
            return TypedResults.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.TileType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["TileType"] = TileTypeEmptyErrors,
            });
        }

        var row = await reports.UpdateEntityAsync(
            id,
            entityId,
            new UpdateEntityInput(request.TileType, request.Title, request.DisplayOrder, request.ConfigJson ?? "{}"),
            cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToEntityDto(row));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, UnauthorizedHttpResult>> RemoveEntityAsync(
        int id,
        int entityId,
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }
        var owned = await CheckOwnedAsync(reports, id, userId, cancellationToken).ConfigureAwait(false);
        if (owned == Ownership.NotFound)
        {
            return TypedResults.NotFound();
        }
        if (owned == Ownership.Forbidden)
        {
            return TypedResults.Forbid();
        }

        var removed = await reports.RemoveEntityAsync(id, entityId, cancellationToken).ConfigureAwait(false);
        return removed ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    // -------------------- Lock / unlock / duplicate --------------------

    private static async Task<Results<Ok<AdminReportsEndpoints.ReportDto>, NotFound, ForbidHttpResult, ValidationProblem, UnauthorizedHttpResult>> LockAsync(
        int id,
        [FromBody] AdminReportsEndpoints.ReportPasswordRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }
        var owned = await CheckOwnedAsync(reports, id, userId, cancellationToken).ConfigureAwait(false);
        if (owned == Ownership.NotFound)
        {
            return TypedResults.NotFound();
        }
        if (owned == Ownership.Forbidden)
        {
            return TypedResults.Forbid();
        }

        var outcome = await reports.LockReportAsync(id, request.Password, cancellationToken).ConfigureAwait(false);
        return outcome.Result switch
        {
            LockResult.Success => TypedResults.Ok(ToReportDto(outcome.Report!)),
            LockResult.NotFound => TypedResults.NotFound(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Password"] = PasswordEmptyErrors,
            }),
        };
    }

    private static async Task<Results<Ok<AdminReportsEndpoints.ReportDto>, NotFound, ForbidHttpResult, ValidationProblem, UnauthorizedHttpResult>> UnlockAsync(
        int id,
        [FromBody] AdminReportsEndpoints.ReportPasswordRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }
        var owned = await CheckOwnedAsync(reports, id, userId, cancellationToken).ConfigureAwait(false);
        if (owned == Ownership.NotFound)
        {
            return TypedResults.NotFound();
        }
        if (owned == Ownership.Forbidden)
        {
            return TypedResults.Forbid();
        }

        var outcome = await reports.UnlockReportAsync(id, request.Password, cancellationToken).ConfigureAwait(false);
        return outcome.Result switch
        {
            UnlockResult.Success => TypedResults.Ok(ToReportDto(outcome.Report!)),
            UnlockResult.NotFound => TypedResults.NotFound(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Password"] = PasswordWrongErrors,
            }),
        };
    }

    private static async Task<Results<Created<AdminReportsEndpoints.ReportDto>, NotFound, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> DuplicateAsync(
        int id,
        [FromBody] AuthorDuplicateReportRequest request,
        ClaimsPrincipal principal,
        IReports reports,
        IAuditLog audit,
        ILogger<AuthorReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetCaller(principal, out var userId, out var displayName))
        {
            return TypedResults.Unauthorized();
        }

        // Authors may clone ANY report (their own or a colleague's) into
        // a fresh copy they own — this matches legacy Vieweb behaviour.
        var source = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"Copy of {source.Report.Title}"
            : request.Title!;

        ReportRow? row;
        try
        {
            row = await reports.DuplicateReportAsync(
                id,
                new DuplicateReportInput(title, userId, displayName),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ReportConflictException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        if (row is null)
        {
            return TypedResults.NotFound();
        }

        LogAuthorReportDuplicated(logger, id, row.Id, userId);
        await audit.WriteAsync(
            AuditEventTypes.ReportDuplicated,
            AuditTargetTypes.Report,
            row.Id.ToString(CultureInfo.InvariantCulture),
            new { sourceId = id, ownerUserId = userId, self = true },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/reports/{row.Id.ToString(CultureInfo.InvariantCulture)}",
            ToReportDto(row));
    }

    // -------------------- Helpers --------------------

    private enum Ownership { Ok, NotFound, Forbidden }

    private static async Task<Ownership> CheckOwnedAsync(
        IReports reports,
        int id,
        int userId,
        CancellationToken cancellationToken)
    {
        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return Ownership.NotFound;
        }
        return detail.Report.OwnerUserId == userId ? Ownership.Ok : Ownership.Forbidden;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out int userId)
    {
        userId = 0;
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
    }

    private static bool TryGetCaller(ClaimsPrincipal principal, out int userId, out string displayName)
    {
        displayName = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? "Author";
        return TryGetUserId(principal, out userId);
    }

    private static AdminReportsEndpoints.ReportDto ToReportDto(ReportRow r) => new(
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

    private static AdminReportsEndpoints.ReportEntityDto ToEntityDto(ReportEntityRow r) => new(
        Id: r.Id,
        ReportId: r.ReportId,
        TileType: r.TileType,
        Title: r.Title,
        DisplayOrder: r.DisplayOrder,
        ConfigJson: r.ConfigJson,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    // -------------------- Logging --------------------

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Author created report {ReportId} (user {UserId})")]
    private static partial void LogAuthorReportCreated(ILogger logger, int reportId, int userId);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Information,
        Message = "Author deleted report {ReportId} (user {UserId})")]
    private static partial void LogAuthorReportDeleted(ILogger logger, int reportId, int userId);

    [LoggerMessage(EventId = 3403, Level = LogLevel.Information,
        Message = "Author duplicated report {SourceId} into {ReportId} (user {UserId})")]
    private static partial void LogAuthorReportDuplicated(ILogger logger, int sourceId, int reportId, int userId);
}
