using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Nieweb.Api.Audit;
using Nieweb.Api.Parameters;
using Nieweb.Api.SkipClassification;
using Nieweb.Api.Startup;
using Nieweb.Data.Entities;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only read / replace for the skip-classification configuration
/// (thresholds + repair-button meaning map). Persisted as the four
/// <c>skip.*</c> rows of the internal <c>AppParameters</c> table; this
/// endpoint presents them as one structured unit (shifts-style atomic
/// replace) so the admin UI can offer typed inputs instead of editing a
/// raw JSON blob. Gated by the <c>Admin</c> role.
/// </summary>
public static partial class AdminSkipClassificationEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminSkipClassificationMarker;

    private static readonly JsonSerializerOptions _mapJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registers <c>/api/admin/skip-classification</c> (GET + PUT).
    /// </summary>
    public static IEndpointRouteBuilder MapAdminSkipClassificationEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/skip-classification")
            .WithTags("AdminSkipClassification")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet(string.Empty, GetAsync).WithName("AdminSkipClassificationGet");
        group.MapPut(string.Empty, ReplaceAsync).WithName("AdminSkipClassificationReplace");

        return routes;
    }

    /// <summary>One repair-button label and the meaning it maps to.</summary>
    public sealed record RepairButtonMeaningDto(string Label, string Meaning);

    /// <summary>The skip-classification config as a single structured unit.</summary>
    public sealed record SkipClassificationConfigDto(
        double MissingRatioThreshold,
        int MinComponentFloor,
        int AbsoluteMissingFloor,
        IReadOnlyList<RepairButtonMeaningDto> RepairButtonMeanings);

    private static async Task<Ok<SkipClassificationConfigDto>> GetAsync(
        ISkipClassificationConfigProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var config = await provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToDto(config));
    }

    private static async Task<Results<Ok<SkipClassificationConfigDto>, ValidationProblem>> ReplaceAsync(
        [FromBody] SkipClassificationConfigDto request,
        IAppParameters parameters,
        ISkipClassificationConfigProvider provider,
        IAuditLog audit,
        ILogger<AdminSkipClassificationMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(audit);

        var errors = Validate(request, out var canonicalMap);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var mapJson = JsonSerializer.Serialize(canonicalMap, _mapJson);

        await UpsertAsync(
            parameters,
            SkipClassificationConfigProvider.MissingRatioThresholdKey,
            AppParameterValueTypes.Decimal,
            request.MissingRatioThreshold.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);
        await UpsertAsync(
            parameters,
            SkipClassificationConfigProvider.MinComponentFloorKey,
            AppParameterValueTypes.Int,
            request.MinComponentFloor.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);
        await UpsertAsync(
            parameters,
            SkipClassificationConfigProvider.AbsoluteMissingFloorKey,
            AppParameterValueTypes.Int,
            request.AbsoluteMissingFloor.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);
        await UpsertAsync(
            parameters,
            SkipClassificationConfigProvider.RepairButtonMeaningsKey,
            AppParameterValueTypes.String,
            mapJson,
            cancellationToken).ConfigureAwait(false);

        LogSkipConfigUpdated(logger, request.MissingRatioThreshold, request.MinComponentFloor, request.AbsoluteMissingFloor);
        await audit.WriteAsync(
            AuditEventTypes.AppParameterUpdated,
            AuditTargetTypes.AppParameter,
            "skip.*",
            new
            {
                missingRatioThreshold = request.MissingRatioThreshold,
                minComponentFloor = request.MinComponentFloor,
                absoluteMissingFloor = request.AbsoluteMissingFloor,
                repairButtonMeanings = canonicalMap,
            },
            cancellationToken).ConfigureAwait(false);

        // Echo back the resolved config (round-trips through the provider
        // so the client sees exactly what the reports will use).
        var resolved = await provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToDto(resolved));
    }

    /// <summary>
    /// Validates the request and, on success, emits the canonicalised
    /// label -&gt; enum-name map to persist. Returns a (possibly empty)
    /// error dictionary keyed by field path.
    /// </summary>
    private static Dictionary<string, string[]> Validate(
        SkipClassificationConfigDto request,
        out Dictionary<string, string> canonicalMap)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        canonicalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (request.MissingRatioThreshold is < 0d or > 1d || double.IsNaN(request.MissingRatioThreshold))
        {
            errors["MissingRatioThreshold"] = ["Must be between 0 and 1."];
        }
        if (request.MinComponentFloor < 1)
        {
            errors["MinComponentFloor"] = ["Must be at least 1."];
        }
        if (request.AbsoluteMissingFloor < 1)
        {
            errors["AbsoluteMissingFloor"] = ["Must be at least 1."];
        }

        var meanings = request.RepairButtonMeanings ?? [];
        for (var i = 0; i < meanings.Count; i++)
        {
            var entry = meanings[i];
            if (string.IsNullOrWhiteSpace(entry.Label))
            {
                errors[$"RepairButtonMeanings[{i}].Label"] = ["Label must not be empty."];
                continue;
            }
            if (!Enum.TryParse<RepairButtonMeaning>(entry.Meaning, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                errors[$"RepairButtonMeanings[{i}].Meaning"] =
                    [$"'{entry.Meaning}' is not a valid meaning. Allowed: {string.Join(", ", Enum.GetNames<RepairButtonMeaning>())}."];
                continue;
            }
            var label = entry.Label.Trim();
            if (canonicalMap.ContainsKey(label))
            {
                errors[$"RepairButtonMeanings[{i}].Label"] = [$"Duplicate label '{label}'."];
                continue;
            }
            canonicalMap[label] = parsed.ToString();
        }

        return errors;
    }

    private static async Task UpsertAsync(
        IAppParameters parameters,
        string key,
        string valueType,
        string value,
        CancellationToken cancellationToken)
    {
        // Preserve the row's existing description (seeded help text or an
        // admin-customised note) rather than wiping it on every save.
        var existing = await parameters.GetAsync(key, cancellationToken).ConfigureAwait(false);
        await parameters
            .UpsertAsync(key, valueType, value, existing?.Description, cancellationToken)
            .ConfigureAwait(false);
    }

    private static SkipClassificationConfigDto ToDto(SkipClassificationConfig config) => new(
        MissingRatioThreshold: config.MissingRatioThreshold,
        MinComponentFloor: config.MinComponentFloor,
        AbsoluteMissingFloor: config.AbsoluteMissingFloor,
        RepairButtonMeanings: config.RepairButtonMeanings
            .Select(kv => new RepairButtonMeaningDto(kv.Key, kv.Value.ToString()))
            .OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
            .ToList());

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Skip-classification config updated: ratio={Ratio} minFloor={MinFloor} absFloor={AbsFloor}")]
    private static partial void LogSkipConfigUpdated(ILogger logger, double ratio, int minFloor, int absFloor);
}
