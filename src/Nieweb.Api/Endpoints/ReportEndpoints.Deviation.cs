using System.Globalization;
using Nieweb.Api.Parameters;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Deviation-chart endpoint (<c>GET /api/reports/deviation</c>) wired
/// over <see cref="DeviationChartReport"/>. Ships CR2 in
/// docs/phase-2.md §7.3.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/deviation</c>. Returns a
    /// <see cref="DeviationResult"/> for the requested source /
    /// window / deviation axis. When no explicit tolerance is
    /// supplied and the axis+opportunity pair maps onto a Vieweb
    /// tolerance-interval parameter (<c>tolerance.{paste|component}.{itx|ity|its}</c>),
    /// the endpoint resolves the symmetric envelope from
    /// <see cref="IAppParameters"/> and passes it to the report.
    /// </summary>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="axis">
    /// Deviation dimension. Accepts kebab-case
    /// (<c>delta-x</c>, <c>delta-y</c>, <c>delta-theta</c>,
    /// <c>delta-thickness</c>, <c>delta-surface</c>) or the raw
    /// <see cref="DeviationAxis"/> member name.
    /// </param>
    /// <param name="opportunity">
    /// One of <c>components</c> (default), <c>paste</c>, or <c>all</c>.
    /// </param>
    /// <param name="binCount">Histogram bin count (1..500, default 40).</param>
    /// <param name="lowerTolerance">
    /// Optional explicit lower tolerance in the axis's own unit (µm
    /// for X / Y / Thickness, degrees for Theta, unitless ratio for
    /// Surface). When both <paramref name="lowerTolerance"/> and
    /// <paramref name="upperTolerance"/> are omitted, the endpoint
    /// looks up a Vieweb-style tolerance interval from
    /// <see cref="IAppParameters"/>.
    /// </param>
    /// <param name="upperTolerance">Symmetric partner of <paramref name="lowerTolerance"/>.</param>
    /// <param name="machineIds">CSV int list, DB-level filter.</param>
    /// <param name="productIds">CSV int list, DB-level filter.</param>
    /// <param name="topologies">CSV list of <c>TESTED_OBJECT.Topology</c> values.</param>
    /// <param name="partNumbers">CSV list of <c>PART_NUMBER</c> names.</param>
    /// <param name="jedecNames">CSV list of <c>JEDEC</c> names.</param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="parameters">Application parameters (for tolerance resolution).</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunDeviationAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? axis,
        string? opportunity,
        int? binCount,
        double? lowerTolerance,
        double? upperTolerance,
        string? machineIds,
        string? productIds,
        string? topologies,
        string? partNumbers,
        string? jedecNames,
        IEnumerable<IAoiSource> sources,
        IAppParameters parameters,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return baseParse.Error;
        }

        if (!TryParseEnumAlias<DeviationAxis>(axis, required: true, out var axisValue, out var error))
        {
            return ProblemFor("axis", error!);
        }
        if (!TryParseEnumAlias<DpmoOpportunity>(opportunity, required: false, out var opportunityValue, out error, defaultValue: DpmoOpportunity.Components))
        {
            return ProblemFor("opportunity", error!);
        }
        var bins = binCount ?? 40;
        if (bins < 1 || bins > 500)
        {
            return ProblemFor("binCount",
                $"must be between 1 and 500 (got {bins.ToString(CultureInfo.InvariantCulture)}).");
        }

        // Only auto-resolve tolerance when the caller supplied neither
        // side explicitly. Half-supplied (only lower or only upper) is
        // a deliberate one-sided overlay — we honour it verbatim.
        double? lower = lowerTolerance;
        double? upper = upperTolerance;
        if (lower is null && upper is null)
        {
            var resolved = await TryResolveToleranceAsync(
                parameters, axisValue, opportunityValue, cancellationToken)
                .ConfigureAwait(false);
            lower = resolved.Lower;
            upper = resolved.Upper;
        }

        var filter = new DeviationFilter(
            Window: baseParse.Window,
            Axis: axisValue,
            Opportunity: opportunityValue,
            BinCount: bins,
            LowerTolerance: lower,
            UpperTolerance: upper,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            Topologies: ParseStringList(topologies),
            PartNumbers: ParseStringList(partNumbers),
            JedecNames: ParseStringList(jedecNames));

        LogRunningDeviation(
            logger,
            baseParse.Source!.Descriptor.Id,
            filter.Axis,
            filter.Opportunity,
            filter.Window.StartUtc,
            filter.Window.EndUtcExclusive);

        try
        {
            var result = await DeviationChartReport.Instance
                .RunAsync(baseParse.Source, filter, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(
                title: "Invalid deviation filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "Invalid deviation filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Maps <paramref name="axis"/> + <paramref name="opportunity"/>
    /// onto a Vieweb tolerance-interval key
    /// (<c>tolerance.{paste|component}.{itx|ity|its}</c>). The stored
    /// value is a full interval in millimetres (X / Y) or square
    /// millimetres (Surface); we convert to µm and return a symmetric
    /// half-interval. A stored value of <c>0</c> is treated as
    /// "not configured" — returns <c>(null, null)</c>. Axes without a
    /// mapped parameter (Theta, Thickness, or opportunity=all) also
    /// return <c>(null, null)</c>.
    /// </summary>
    private static async Task<(double? Lower, double? Upper)> TryResolveToleranceAsync(
        IAppParameters parameters,
        DeviationAxis axis,
        DpmoOpportunity opportunity,
        CancellationToken ct)
    {
        var opportunitySlug = opportunity switch
        {
            DpmoOpportunity.Components => "component",
            DpmoOpportunity.Paste => "paste",
            _ => null,
        };
        if (opportunitySlug is null)
        {
            return (null, null);
        }

        // Surface is unitless in the DTO but Vieweb stores its
        // tolerance interval as a mm² area. Rather than paper over
        // the mismatch we just do not auto-resolve for surface — the
        // caller must pass explicit bounds.
        var (axisSlug, umPerUnit) = axis switch
        {
            DeviationAxis.DeltaX => ("itx", 1000d),   // mm  → µm
            DeviationAxis.DeltaY => ("ity", 1000d),   // mm  → µm
            _ => (null, 0d),
        };
        if (axisSlug is null)
        {
            return (null, null);
        }

        var key = "tolerance." + opportunitySlug + "." + axisSlug;
        var row = await parameters.GetAsync(key, ct).ConfigureAwait(false);
        if (row is null)
        {
            return (null, null);
        }
        decimal it;
        try
        {
            it = row.AsDecimal();
        }
        catch (InvalidOperationException)
        {
            return (null, null);
        }
        if (it <= 0m)
        {
            return (null, null);
        }
        var half = (double)it * umPerUnit / 2d;
        return (-half, +half);
    }

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Running deviation on '{SourceId}' axis={Axis} opportunity={Opportunity} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningDeviation(
        ILogger logger,
        string sourceId,
        DeviationAxis axis,
        DpmoOpportunity opportunity,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);
}
