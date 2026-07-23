using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.DataSources;
using Nieweb.Api.Startup;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only CRUD for <see cref="Nieweb.Data.Entities.AoiSourceConfig"/>
/// rows plus "Test connection" and "Restart API" endpoints
/// (Phase C — Databases settings screen).
/// </summary>
/// <remarks>
/// <para>
/// Every mutating call emits an audit event and, when the mutation
/// changes something that would alter the live sources, arms the
/// process-wide <see cref="IPendingRestartSignal"/> so the UI can
/// prompt the operator to restart.
/// </para>
/// <para>
/// Passwords are never returned. On update, an empty
/// <see cref="AoiSourceConfigSpec.Password"/> preserves the existing
/// encrypted blob so operators can edit metadata without re-typing.
/// </para>
/// </remarks>
public static partial class AdminDataSourcesEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class Marker;

    /// <summary>Registers the <c>/api/admin/data-sources</c> endpoints.</summary>
    public static IEndpointRouteBuilder MapAdminDataSourcesEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/data-sources")
            .WithTags("AdminDataSources")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminDataSourcesList");
        group.MapGet("/{key}", GetAsync).WithName("AdminDataSourcesGet");
        group.MapPut("/{key}", UpsertAsync).WithName("AdminDataSourcesUpsert");
        group.MapDelete("/{key}", DeleteAsync).WithName("AdminDataSourcesDelete");
        group.MapPost("/test", TestAsync).WithName("AdminDataSourcesTest");
        group.MapPost("/restart", RestartAsync).WithName("AdminDataSourcesRestart");
        group.MapGet("/restart-status", RestartStatusAsync).WithName("AdminDataSourcesRestartStatus");

        return routes;
    }

    /// <summary>Response for <c>GET /restart-status</c>.</summary>
    public sealed record RestartStatusResponse(bool Pending, DateTime? SetUtc, string? Reason);

    private static async Task<Ok<IReadOnlyList<AoiSourceConfigView>>> ListAsync(
        IAoiSourceConfigs service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        var rows = await service.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<AoiSourceConfigView>, NotFound>> GetAsync(
        string key,
        IAoiSourceConfigs service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        var row = await service.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return row is null ? TypedResults.NotFound() : TypedResults.Ok(row);
    }

    /// <summary>
    /// Payload accepted by <c>PUT /{key}</c>. The route <c>{key}</c>
    /// wins - the body's <c>Key</c> is ignored (kept optional so a
    /// single DTO can serve create + update on the client side).
    /// </summary>
    public sealed record UpsertRequest(
        string? DisplayName,
        string? Kind,
        string? Server,
        string? Database,
        string? User,
        string? Password,
        int? ConnectTimeoutSeconds,
        int? QueryTimeoutSeconds,
        bool? TrustServerCertificate,
        bool? Encrypt,
        bool? IsEnabled);

    private static async Task<Results<Ok<AoiSourceConfigView>, ValidationProblem, Conflict<string>>> UpsertAsync(
        string key,
        [FromBody] UpsertRequest request,
        IAoiSourceConfigs service,
        IPendingRestartSignal restart,
        IAuditLog audit,
        TimeProvider time,
        ILogger<Marker> logger,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpRequest);

        var spec = new AoiSourceConfigSpec(
            Key: key,
            DisplayName: request.DisplayName ?? string.Empty,
            Kind: request.Kind ?? string.Empty,
            Server: request.Server,
            Database: request.Database,
            User: request.User,
            Password: request.Password,
            ConnectTimeoutSeconds: request.ConnectTimeoutSeconds ?? 15,
            QueryTimeoutSeconds: request.QueryTimeoutSeconds ?? 30,
            TrustServerCertificate: request.TrustServerCertificate ?? true,
            Encrypt: request.Encrypt ?? false,
            IsEnabled: request.IsEnabled ?? true);

        var before = await service.GetAsync(key, cancellationToken).ConfigureAwait(false);

        // RFC 7232 §3.2: `If-None-Match: *` on PUT means "only if the
        // resource does not currently exist" — i.e. treat this as a
        // create-only request. This closes the race where two admins
        // concurrently open the "Add database" modal with the same key
        // and both press Create — without it, the second PUT would
        // silently overwrite the first. The client sends this header
        // exclusively in create mode; edit mode never sends it.
        if (before is not null && IsCreateOnly(httpRequest))
        {
            return TypedResults.Conflict($"A database with key '{key}' already exists.");
        }

        AoiSourceConfigView view;
        try
        {
            view = await service.UpsertAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            var errors = new Dictionary<string, string[]>
            {
                [string.Empty] = new[] { ex.Message },
            };
            return TypedResults.ValidationProblem(errors);
        }

        var eventType = before is null ? AuditEventTypes.DataSourceCreated : AuditEventTypes.DataSourceUpdated;
        await audit.WriteAsync(
            eventType,
            AuditTargetTypes.DataSource,
            view.Key,
            new
            {
                before = before is null ? null : Sanitise(before),
                after = Sanitise(view),
                passwordChanged = !string.IsNullOrEmpty(request.Password),
            },
            cancellationToken).ConfigureAwait(false);

        restart.MarkPending(
            reason: before is null ? $"created '{view.Key}'" : $"updated '{view.Key}'",
            nowUtc: time.GetUtcNow().UtcDateTime);
        LogUpserted(logger, view.Key);
        return TypedResults.Ok(view);
    }

    /// <summary>
    /// True when the request carries an <c>If-None-Match: *</c> header
    /// (any capitalisation, whitespace tolerated). Per RFC 7232 §3.2
    /// this means "only apply the PUT if the target resource does not
    /// yet exist" — the standard HTTP idiom for a race-free create via
    /// an otherwise-idempotent PUT.
    /// </summary>
    private static bool IsCreateOnly(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var values))
        {
            return false;
        }
        foreach (var value in values)
        {
            if (value is not null && value.AsSpan().Trim().SequenceEqual("*".AsSpan()))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        string key,
        IAoiSourceConfigs service,
        IPendingRestartSignal restart,
        IAuditLog audit,
        TimeProvider time,
        ILogger<Marker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(time);

        var before = await service.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return TypedResults.NotFound();
        }

        var deleted = await service.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return TypedResults.NotFound();
        }

        await audit.WriteAsync(
            AuditEventTypes.DataSourceDeleted,
            AuditTargetTypes.DataSource,
            key,
            new { before = Sanitise(before) },
            cancellationToken).ConfigureAwait(false);

        restart.MarkPending(reason: $"deleted '{key}'", nowUtc: time.GetUtcNow().UtcDateTime);
        LogDeleted(logger, key);
        return TypedResults.NoContent();
    }

    /// <summary>Test-connection request. Same shape as upsert but requires <c>Key</c>.</summary>
    public sealed record TestRequest(
        string Key,
        string? DisplayName,
        string? Kind,
        string? Server,
        string? Database,
        string? User,
        string? Password,
        int? ConnectTimeoutSeconds,
        int? QueryTimeoutSeconds,
        bool? TrustServerCertificate,
        bool? Encrypt,
        bool? IsEnabled);

    private static async Task<Results<Ok<AoiSourceTestResult>, ValidationProblem>> TestAsync(
        [FromBody] TestRequest request,
        IAoiSourceConfigs service,
        IAuditLog audit,
        ILogger<Marker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        var spec = new AoiSourceConfigSpec(
            Key: request.Key ?? string.Empty,
            DisplayName: request.DisplayName ?? "test",
            Kind: request.Kind ?? string.Empty,
            Server: request.Server,
            Database: request.Database,
            User: request.User,
            Password: request.Password,
            ConnectTimeoutSeconds: request.ConnectTimeoutSeconds ?? 15,
            QueryTimeoutSeconds: request.QueryTimeoutSeconds ?? 30,
            TrustServerCertificate: request.TrustServerCertificate ?? true,
            Encrypt: request.Encrypt ?? false,
            IsEnabled: request.IsEnabled ?? true);

        AoiSourceTestResult result;
        try
        {
            result = await service.TestAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            var errors = new Dictionary<string, string[]>
            {
                [string.Empty] = new[] { ex.Message },
            };
            return TypedResults.ValidationProblem(errors);
        }

        await audit.WriteAsync(
            AuditEventTypes.DataSourceTested,
            AuditTargetTypes.DataSource,
            spec.Key,
            new
            {
                ok = result.Ok,
                durationMs = result.DurationMs,
                errorMessage = result.ErrorMessage,
                usedProvidedPassword = !string.IsNullOrEmpty(request.Password),
            },
            cancellationToken).ConfigureAwait(false);

        LogTested(logger, spec.Key, result.Ok, result.DurationMs);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<object>> RestartAsync(
        IAuditLog audit,
        IHostApplicationLifetime lifetime,
        ILogger<Marker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(lifetime);

        await audit.WriteAsync(
            AuditEventTypes.DataSourceRestartRequested,
            AuditTargetTypes.DataSource,
            "*",
            details: null,
            cancellationToken).ConfigureAwait(false);

        LogRestartRequested(logger);

        // Fire-and-forget: give the response time to reach the client
        // before we stop the host, otherwise the client sees a dropped
        // connection instead of a 200. Errors are swallowed - the whole
        // point is to exit the process. Passing CancellationToken.None
        // is intentional - once the response is written we want the
        // shutdown to happen regardless of the request's cancellation.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
                lifetime.StopApplication();
            }
            catch (Exception ex)
            {
                LogRestartFailed(logger, ex);
            }
        }, CancellationToken.None);

        return TypedResults.Ok<object>(new { ok = true, message = "Restart requested." });
    }

    private static Ok<RestartStatusResponse> RestartStatusAsync(IPendingRestartSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return TypedResults.Ok(new RestartStatusResponse(signal.IsPending, signal.SetUtc, signal.Reason));
    }

    /// <summary>Strip credentials + password markers from an audit payload.</summary>
    private static object Sanitise(AoiSourceConfigView v) => new
    {
        v.Key,
        v.DisplayName,
        v.Kind,
        v.Server,
        v.Database,
        v.User,
        v.HasPassword,
        v.ConnectTimeoutSeconds,
        v.QueryTimeoutSeconds,
        v.TrustServerCertificate,
        v.Encrypt,
        v.IsEnabled,
    };

    [LoggerMessage(EventId = 3601, Level = LogLevel.Information,
        Message = "AOI data source '{Key}' upserted; restart is now pending.")]
    private static partial void LogUpserted(ILogger logger, string key);

    [LoggerMessage(EventId = 3602, Level = LogLevel.Information,
        Message = "AOI data source '{Key}' deleted; restart is now pending.")]
    private static partial void LogDeleted(ILogger logger, string key);

    [LoggerMessage(EventId = 3603, Level = LogLevel.Information,
        Message = "AOI test '{Key}' -> ok={Ok} in {DurationMs}ms")]
    private static partial void LogTested(ILogger logger, string key, bool ok, long durationMs);

    [LoggerMessage(EventId = 3604, Level = LogLevel.Warning,
        Message = "Admin requested API restart from Databases screen; shutting down in 500ms.")]
    private static partial void LogRestartRequested(ILogger logger);

    [LoggerMessage(EventId = 3605, Level = LogLevel.Error,
        Message = "Restart trigger failed unexpectedly.")]
    private static partial void LogRestartFailed(ILogger logger, Exception ex);
}
