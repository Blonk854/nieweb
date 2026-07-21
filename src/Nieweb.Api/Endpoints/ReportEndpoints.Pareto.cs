using System.Globalization;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Volume-weighted Pareto endpoint
/// (<c>GET /api/reports/pareto</c>) wired over
/// <see cref="ParetoReport"/>.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/pareto</c>. Returns a
    /// <see cref="ParetoResult"/> for the requested source / window /
    /// axis. Every narrowing collection combines as a logical AND so
    /// the client can drill any depth simply by adding one more
    /// filter value per call — no server-side session state is
    /// required.
    /// </summary>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="axis">
    /// Group-by axis. Accepts kebab-case
    /// (<c>defect</c>, <c>product</c>, <c>aoi-machine</c>,
    /// <c>reference-designator</c>, <c>part-number</c>, <c>jedec</c>)
    /// or the raw <see cref="ParetoAxis"/> member name.
    /// </param>
    /// <param name="numerator">One of <c>real</c> (default), <c>aoi</c>, <c>dummy</c>.</param>
    /// <param name="opportunity">One of <c>all</c> (default), <c>components</c>, <c>paste</c>.</param>
    /// <param name="weight">Only <c>count</c> ships today.</param>
    /// <param name="topN">Optional cap on visible rows.</param>
    /// <param name="includeOthers">
    /// When <c>true</c> (default) and <paramref name="topN"/> caused
    /// overflow, collapse the surplus into a single "Others" row.
    /// </param>
    /// <param name="vitalFewThreshold">
    /// Cumulative-% cut-off for the vital-few flag. Default 80.0.
    /// </param>
    /// <param name="includeObsoleteBits">
    /// When axis is <c>defect</c>, whether to emit rows for obsolete
    /// bits. Default <c>false</c>.
    /// </param>
    /// <param name="machineIds">CSV int list, filtered at the DB level.</param>
    /// <param name="productIds">CSV int list, filtered at the DB level.</param>
    /// <param name="recipeIds">CSV int list, filtered at the DB level.</param>
    /// <param name="defectBits">
    /// CSV int list of 1-based bit numbers (1..25). Narrows the input
    /// to tested-object rows carrying at least one of these bits.
    /// </param>
    /// <param name="topologies">CSV list of <c>TESTED_OBJECT.Topology</c> values.</param>
    /// <param name="partNumbers">CSV list of <c>PART_NUMBER</c> names.</param>
    /// <param name="jedecNames">CSV list of <c>JEDEC</c> names.</param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunParetoAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? axis,
        string? numerator,
        string? opportunity,
        string? weight,
        int? topN,
        bool? includeOthers,
        double? vitalFewThreshold,
        bool? includeObsoleteBits,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        string? defectBits,
        string? topologies,
        string? partNumbers,
        string? jedecNames,
        IEnumerable<IAoiSource> sources,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var built = TryBuildParetoRequest(
            sourceId, startUtc, endUtc, axis, numerator, opportunity, weight,
            topN, includeOthers, vitalFewThreshold, includeObsoleteBits,
            machineIds, productIds, recipeIds,
            defectBits, topologies, partNumbers, jedecNames,
            sources);
        if (built.Error is not null)
        {
            return built.Error;
        }

        LogRunningPareto(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.Axis,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        try
        {
            var result = await ParetoReport.Instance
                .RunAsync(built.Source, built.Filter, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // ParetoReport validates TopN and VitalFewThresholdPercent.
            return Results.Problem(
                title: "Invalid Pareto filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (NotSupportedException ex)
        {
            // Non-Count ParetoWeight is not implemented yet.
            return Results.Problem(
                title: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static (IAoiSource? Source, ParetoFilter? Filter, IResult? Error) TryBuildParetoRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? axis,
        string? numerator,
        string? opportunity,
        string? weight,
        int? topN,
        bool? includeOthers,
        double? vitalFewThreshold,
        bool? includeObsoleteBits,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        string? defectBits,
        string? topologies,
        string? partNumbers,
        string? jedecNames,
        IEnumerable<IAoiSource> sources)
    {
        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return (null, null, baseParse.Error);
        }

        if (!TryParseEnumAlias<ParetoAxis>(axis, required: true, out var axisValue, out var error))
        {
            return (null, null, ProblemFor("axis", error!));
        }
        if (!TryParseEnumAlias<DpmoNumerator>(numerator, required: false, out var numeratorValue, out error, defaultValue: DpmoNumerator.Real))
        {
            return (null, null, ProblemFor("numerator", error!));
        }
        if (!TryParseEnumAlias<DpmoOpportunity>(opportunity, required: false, out var opportunityValue, out error, defaultValue: DpmoOpportunity.All))
        {
            return (null, null, ProblemFor("opportunity", error!));
        }
        if (!TryParseEnumAlias<ParetoWeight>(weight, required: false, out var weightValue, out error, defaultValue: ParetoWeight.Count))
        {
            return (null, null, ProblemFor("weight", error!));
        }

        if (topN is int t and <= 0)
        {
            return (null, null, ProblemFor("topN",
                $"must be a positive integer (got {t.ToString(CultureInfo.InvariantCulture)})."));
        }

        var threshold = vitalFewThreshold ?? 80.0;
        if (threshold < 0 || threshold > 100)
        {
            return (null, null, ProblemFor("vitalFewThreshold",
                $"must be between 0 and 100 (got {threshold.ToString(CultureInfo.InvariantCulture)})."));
        }

        List<int>? defectBitList = ParseIntList(defectBits);
        if (defectBitList is not null)
        {
            foreach (var b in defectBitList)
            {
                if (b < 1 || b > 25)
                {
                    return (null, null, ProblemFor("defectBits",
                        $"'{b.ToString(CultureInfo.InvariantCulture)}' is out of range. Values must be 1..25 (DefectBitDecoder catalogue)."));
                }
            }
        }

        var filter = new ParetoFilter(
            Window: baseParse.Window,
            Axis: axisValue,
            Numerator: numeratorValue,
            Opportunity: opportunityValue,
            Weight: weightValue,
            TopN: topN,
            IncludeOthersBucket: includeOthers ?? true,
            VitalFewThresholdPercent: threshold,
            IncludeObsoleteBits: includeObsoleteBits ?? false,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            RecipeIds: ParseIntList(recipeIds),
            DefectBits: defectBitList,
            Topologies: ParseStringList(topologies),
            PartNumbers: ParseStringList(partNumbers),
            JedecNames: ParseStringList(jedecNames));

        return (baseParse.Source, filter, null);
    }

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Running pareto on '{SourceId}' axis={Axis} numerator={Numerator} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningPareto(
        ILogger logger,
        string sourceId,
        ParetoAxis axis,
        DpmoNumerator numerator,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);
}
