using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.Parameters;
using Nieweb.Api.Startup;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only CRUD for the internal <c>AppParameters</c> table (RI3 of
/// docs/phase-2.md §7.1). Routes are gated by the <c>Admin</c> role and
/// backed by <see cref="IAppParameters"/>.
/// </summary>
/// <remarks>
/// <para>
/// Parity notes vs Vieweb §2.4.2 (Application parameters):
/// tolerance intervals (paste + component), GR&amp;R constant, confidence
/// coefficient, and Tolerance EV are all seeded as system rows and can
/// be updated but not deleted. The global <c>batch.enabled</c> switch
/// (Vieweb <c>batchIsOn</c>) is also a system row and is updated here
/// rather than through a dedicated screen — the automatic-treatment
/// scheduler (F3 / AT2) reads it every wake-up.
/// </para>
/// <para>
/// Every write emits an audit row
/// (<see cref="AuditEventTypes.AppParameterCreated"/>,
/// <see cref="AuditEventTypes.AppParameterUpdated"/>,
/// <see cref="AuditEventTypes.AppParameterDeleted"/>) with before / after
/// snapshots so the admin audit page (I4) surfaces every knob change.
/// </para>
/// </remarks>
public static partial class AdminParametersEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminParametersMarker;

    private static readonly string[] KeyEmptyErrors = new[] { "Key must not be empty." };

    /// <summary>
    /// Registers the <c>/api/admin/parameters</c> endpoints on
    /// <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminParametersEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/parameters")
            .WithTags("AdminParameters")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, ListAsync).WithName("AdminParametersList");
        group.MapGet("/{key}", GetAsync).WithName("AdminParametersGet");
        group.MapPut("/{key}", UpsertAsync).WithName("AdminParametersUpsert");
        group.MapDelete("/{key}", DeleteAsync).WithName("AdminParametersDelete");

        return routes;
    }

    /// <summary>Row DTO returned by list + get + upsert.</summary>
    public sealed record AdminParameterDto(
        string Key,
        string ValueType,
        string Value,
        string? Description,
        bool IsSystem,
        DateTime CreatedUtc,
        DateTime LastModifiedUtc);

    /// <summary>PUT payload for create-or-update.</summary>
    public sealed record UpsertParameterRequest
    {
        /// <summary>
        /// One of <see cref="AppParameterValueTypes.Decimal"/>,
        /// <see cref="AppParameterValueTypes.Int"/>,
        /// <see cref="AppParameterValueTypes.Bool"/>,
        /// <see cref="AppParameterValueTypes.String"/>.
        /// </summary>
        [Required, StringLength(16)]
        public string ValueType { get; init; } = string.Empty;

        /// <summary>Invariant-culture text form of the value.</summary>
        [Required, StringLength(2048)]
        public string Value { get; init; } = string.Empty;

        /// <summary>Optional human-readable description.</summary>
        [StringLength(500)]
        public string? Description { get; init; }
    }

    private static async Task<Ok<IReadOnlyList<AdminParameterDto>>> ListAsync(
        IAppParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var rows = await parameters.ListAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(ToDto).ToList();
        return TypedResults.Ok((IReadOnlyList<AdminParameterDto>)dtos);
    }

    private static async Task<Results<Ok<AdminParameterDto>, NotFound>> GetAsync(
        string key,
        IAppParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var row = await parameters.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return row is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<Ok<AdminParameterDto>, Created<AdminParameterDto>, ValidationProblem>> UpsertAsync(
        string key,
        [FromBody] UpsertParameterRequest request,
        IAppParameters parameters,
        IAuditLog audit,
        ILogger<AdminParametersMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(key))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Key"] = KeyEmptyErrors,
            });
        }

        var before = await parameters.GetAsync(key, cancellationToken).ConfigureAwait(false);

        AppParameterUpsertResult result;
        try
        {
            result = await parameters
                .UpsertAsync(key, request.ValueType, request.Value, request.Description, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // Value-type / value parsing failures surface as 400 with
            // a message the admin UI can show verbatim.
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "Value"] = new[] { ex.Message },
            });
        }

        var dto = ToDto(result.Row);
        if (result.Created)
        {
            LogParameterCreated(logger, key);
            await audit.WriteAsync(
                AuditEventTypes.AppParameterCreated,
                AuditTargetTypes.AppParameter,
                key,
                new
                {
                    valueType = result.Row.ValueType,
                    value = result.Row.Value,
                    description = result.Row.Description,
                },
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Created($"/api/admin/parameters/{Uri.EscapeDataString(key)}", dto);
        }

        LogParameterUpdated(logger, key);
        await audit.WriteAsync(
            AuditEventTypes.AppParameterUpdated,
            AuditTargetTypes.AppParameter,
            key,
            new
            {
                before = before is null
                    ? null
                    : new { before.ValueType, before.Value, before.Description },
                after = new
                {
                    valueType = result.Row.ValueType,
                    value = result.Row.Value,
                    description = result.Row.Description,
                },
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(dto);
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> DeleteAsync(
        string key,
        IAppParameters parameters,
        IAuditLog audit,
        ILogger<AdminParametersMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(audit);

        var before = await parameters.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return TypedResults.NotFound();
        }

        bool removed;
        try
        {
            removed = await parameters.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // IsSystem=true rows refuse deletion.
            return TypedResults.Conflict(ex.Message);
        }

        if (!removed)
        {
            return TypedResults.NotFound();
        }

        LogParameterDeleted(logger, key);
        await audit.WriteAsync(
            AuditEventTypes.AppParameterDeleted,
            AuditTargetTypes.AppParameter,
            key,
            new
            {
                valueType = before.ValueType,
                value = before.Value,
                description = before.Description,
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static AdminParameterDto ToDto(AppParameterRow r) => new(
        Key: r.Key,
        ValueType: r.ValueType,
        Value: r.Value,
        Description: r.Description,
        IsSystem: r.IsSystem,
        CreatedUtc: r.CreatedUtc,
        LastModifiedUtc: r.LastModifiedUtc);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "Admin created app parameter {Key}")]
    private static partial void LogParameterCreated(ILogger logger, string key);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information,
        Message = "Admin updated app parameter {Key}")]
    private static partial void LogParameterUpdated(ILogger logger, string key);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Information,
        Message = "Admin deleted app parameter {Key}")]
    private static partial void LogParameterDeleted(ILogger logger, string key);
}
