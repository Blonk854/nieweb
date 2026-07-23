using Nieweb.Api.Shifts;
using Nieweb.DataSources;
using Nieweb.Reports;
using Nieweb.Reports.Common;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Trend-chart endpoint (<c>GET /api/reports/trend</c>) wired over
/// <see cref="TrendChartReport"/>. Ships CR3 in docs/phase-2.md §7.3.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/trend</c>. Returns a
    /// <see cref="TrendResult"/> for the requested source, window,
    /// bucket size, and metric set. When
    /// <paramref name="bucket"/> is <c>shift</c> and no explicit
    /// <paramref name="shifts"/> is supplied, the endpoint pulls the
    /// site-wide shift definition from <see cref="IShifts"/>.
    /// </summary>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="bucket">
    /// Time-bucket size. Accepts kebab-case
    /// (<c>hour-1</c>, <c>hour-3</c>, <c>hour-6</c>, <c>hour-12</c>,
    /// <c>shift</c>, <c>day</c>, <c>week</c>, <c>month</c>) or the
    /// raw <see cref="TimeBucket"/> member name.
    /// </param>
    /// <param name="metrics">
    /// CSV list of metrics (kebab-case slugs such as <c>fpy-aoi</c>,
    /// <c>dpmo-real</c>, <c>panel-count</c>, <c>cp</c>). At least one
    /// metric is required.
    /// </param>
    /// <param name="numerator">Numerator used by <c>defect-count</c> (default <c>real</c>).</param>
    /// <param name="opportunity">Opportunity filter (default <c>all</c>).</param>
    /// <param name="deviationAxis">
    /// Required when <c>cp</c> or <c>cpk</c> is in the metric set.
    /// Same slug set as the deviation endpoint.
    /// </param>
    /// <param name="lowerTolerance">Lower spec limit for Cp / Cpk.</param>
    /// <param name="upperTolerance">Upper spec limit for Cp / Cpk.</param>
    /// <param name="machineIds">CSV int list, DB-level filter.</param>
    /// <param name="productIds">CSV int list, DB-level filter.</param>
    /// <param name="topologies">CSV string list, in-memory narrowing.</param>
    /// <param name="partNumbers">CSV string list, in-memory narrowing.</param>
    /// <param name="jedecNames">CSV string list, in-memory narrowing.</param>
    /// <param name="siteTimeZone">
    /// IANA or Windows time-zone id for bucket alignment. Defaults to
    /// UTC when omitted — matching the Superviseur DB's storage
    /// convention.
    /// </param>
    /// <param name="shifts">
    /// CSV of HH:MM shift start times. When omitted and
    /// <paramref name="bucket"/> is <c>shift</c>, the endpoint falls
    /// back to <see cref="IShifts.BuildShiftDefinitionAsync"/>.
    /// </param>
    /// <param name="onlyLastInspection">
    /// When <c>true</c> (default), restrict panel-level metrics to
    /// each panel's latest inspection on sources that support
    /// <see cref="Capabilities.IsLastInspectionFilter"/>.
    /// </param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="shiftsProvider">Site-wide shift definition provider.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunTrendAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? metrics,
        string? numerator,
        string? opportunity,
        string? deviationAxis,
        double? lowerTolerance,
        double? upperTolerance,
        string? machineIds,
        string? productIds,
        string? topologies,
        string? partNumbers,
        string? jedecNames,
        string? siteTimeZone,
        string? shifts,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        IShifts shiftsProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shiftsProvider);

        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return baseParse.Error;
        }

        if (!TryParseEnumAlias<TimeBucket>(bucket, required: true, out var bucketValue, out var error))
        {
            return ProblemFor("bucket", error!);
        }
        if (!TryParseEnumAlias<DpmoNumerator>(numerator, required: false, out var numeratorValue, out error, defaultValue: DpmoNumerator.Real))
        {
            return ProblemFor("numerator", error!);
        }
        if (!TryParseEnumAlias<DpmoOpportunity>(opportunity, required: false, out var opportunityValue, out error, defaultValue: DpmoOpportunity.All))
        {
            return ProblemFor("opportunity", error!);
        }

        var metricSet = ParseMetricList(metrics, out var metricError);
        if (metricError is not null)
        {
            return ProblemFor("metrics", metricError);
        }
        if (metricSet is null || metricSet.Count == 0)
        {
            return ProblemFor("metrics", "at least one metric slug is required (e.g. metrics=fpy-aoi,dpmo-real).");
        }

        DeviationAxis? axisValue = null;
        if (!string.IsNullOrWhiteSpace(deviationAxis))
        {
            if (!TryParseEnumAlias<DeviationAxis>(deviationAxis, required: true, out var parsedAxis, out error))
            {
                return ProblemFor("deviationAxis", error!);
            }
            axisValue = parsedAxis;
        }

        var siteTz = TryParseTimeZone(siteTimeZone, out var tzError);
        if (tzError is not null)
        {
            return ProblemFor("siteTimeZone", tzError);
        }

        var shiftDef = TryParseShifts(shifts, out var shiftsError);
        if (shiftsError is not null)
        {
            return ProblemFor("shifts", shiftsError);
        }
        if (bucketValue == TimeBucket.Shift && shiftDef is null)
        {
            // Fall back to the site-wide shift cycle configured under
            // /api/admin/shifts. If none exists, that's an error the
            // caller can fix by supplying the shifts=HH:MM,... query
            // parameter or configuring the site cycle.
            shiftDef = await shiftsProvider.BuildShiftDefinitionAsync(cancellationToken).ConfigureAwait(false);
            if (shiftDef is null)
            {
                return ProblemFor("shifts",
                    "bucket=shift requires either a shifts=HH:MM,... query parameter or a configured site shift cycle.");
            }
        }

        var filter = new TrendFilter(
            Window: baseParse.Window,
            Bucket: bucketValue,
            Metrics: metricSet,
            Numerator: numeratorValue,
            Opportunity: opportunityValue,
            DeviationAxis: axisValue,
            LowerTolerance: lowerTolerance,
            UpperTolerance: upperTolerance,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            Topologies: ParseStringList(topologies),
            PartNumbers: ParseStringList(partNumbers),
            JedecNames: ParseStringList(jedecNames),
            SiteTimeZone: siteTz,
            Shifts: shiftDef,
            OnlyLastInspection: onlyLastInspection ?? true);

        LogRunningTrend(
            logger,
            baseParse.Source!.Descriptor.Id,
            filter.Bucket,
            metricSet.Count,
            filter.Window.StartUtc,
            filter.Window.EndUtcExclusive);

        try
        {
            var result = await TrendChartReport.Instance
                .RunAsync(baseParse.Source, filter, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(
                title: "Invalid trend filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "Invalid trend filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Parse a CSV list of trend-metric slugs. Kebab-case is accepted
    /// (e.g. <c>fpy-aoi</c>, <c>dpmo-real</c>, <c>panel-count</c>) as
    /// well as the raw <see cref="TrendMetric"/> member names.
    /// Duplicates are silently deduped; the first occurrence wins.
    /// </summary>
    private static List<TrendMetric>? ParseMetricList(string? raw, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }
        var result = new List<TrendMetric>(parts.Length);
        var seen = new HashSet<TrendMetric>();
        foreach (var part in parts)
        {
            if (!TryParseEnumAlias<TrendMetric>(part, required: true, out var value, out var slugError))
            {
                error = $"'{part}' is not a valid TrendMetric ({slugError}).";
                return null;
            }
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }
        return result;
    }

    [LoggerMessage(EventId = 3402, Level = LogLevel.Information,
        Message = "Running trend on '{SourceId}' bucket={Bucket} metrics={MetricCount} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningTrend(
        ILogger logger,
        string sourceId,
        TimeBucket bucket,
        int metricCount,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);
}
