using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using ClosedXML.Excel;
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

    // -------------------------------------------------------------------------
    // Export endpoints
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>GET /api/reports/pareto/export.csv</c>. Same query contract
    /// as <see cref="RunParetoAsync"/>. Streams a UTF-8 (BOM-prefixed)
    /// CSV with one header row, one row per visible bar (sorted
    /// descending by <see cref="ParetoRow.WeightedScore"/>), and — if
    /// TopN caused overflow — one final <c>OTHERS</c> row.
    /// </summary>
    /// <param name="context">Ambient <see cref="HttpContext"/>.</param>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="axis">Group-by axis (kebab-case slug or enum name).</param>
    /// <param name="numerator">Numerator flavour (default <c>real</c>).</param>
    /// <param name="opportunity">Opportunity filter (default <c>all</c>).</param>
    /// <param name="weight">Weight metric (only <c>count</c> ships today).</param>
    /// <param name="topN">Optional cap on visible rows.</param>
    /// <param name="includeOthers">Collapse overflow into a synthetic Others row (default <c>true</c>).</param>
    /// <param name="vitalFewThreshold">Vital-few cumulative-% cut-off (default 80).</param>
    /// <param name="includeObsoleteBits">Include obsolete defect bits when axis=defect.</param>
    /// <param name="machineIds">CSV int list.</param>
    /// <param name="productIds">CSV int list.</param>
    /// <param name="recipeIds">CSV int list.</param>
    /// <param name="defectBits">CSV int list (1..25).</param>
    /// <param name="topologies">CSV string list.</param>
    /// <param name="partNumbers">CSV string list.</param>
    /// <param name="jedecNames">CSV string list.</param>
    /// <param name="sources">All registered AOI sources.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task ExportParetoCsvAsync(
        HttpContext context,
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
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildParetoRequest(
            sourceId, startUtc, endUtc, axis, numerator, opportunity, weight,
            topN, includeOthers, vitalFewThreshold, includeObsoleteBits,
            machineIds, productIds, recipeIds,
            defectBits, topologies, partNumbers, jedecNames,
            sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunningPareto(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.Axis,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        ParetoResult result;
        try
        {
            result = await ParetoReport.Instance
                .RunAsync(built.Source, built.Filter, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await Results.Problem(
                title: "Invalid Pareto filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        catch (NotSupportedException ex)
        {
            await Results.Problem(
                title: ex.Message,
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"pareto-{built.Source.Descriptor.Id}-{result.Axis}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.csv");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await WriteParetoCsvAsync(context.Response.BodyWriter, result, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteParetoCsvAsync(
        PipeWriter writer, ParetoResult result, CancellationToken ct)
    {
        await writer.WriteAsync(Utf8Bom, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.Append("SourceId,SourceName,WindowStartUtc,WindowEndUtc,Axis,Numerator,Opportunity,Weight,")
          .Append("Rank,GroupKey,GroupName,DefectCount,WeightedScore,OpportunityCount,")
          .Append("OpportunitySharePercent,DpmoPpm,DefectSharePercent,CumulativePercent,IsVitalFew\r\n");
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        var sourceId = CsvEscape(result.Source.Id);
        var sourceName = CsvEscape(result.Source.DisplayName);
        var startIso = result.Window.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var endIso = result.Window.EndUtcExclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var axis = result.Axis.ToString();
        var numerator = result.Numerator.ToString();
        var opportunity = result.Opportunity.ToString();
        var weight = result.Weight.ToString();

        var rank = 1;
        foreach (var row in result.Rows)
        {
            AppendParetoRow(sb, sourceId, sourceName, startIso, endIso, axis, numerator, opportunity, weight,
                rank.ToString(CultureInfo.InvariantCulture), row.GroupKey, row.GroupName, row);
            await FlushAsync(writer, sb, ct).ConfigureAwait(false);
            rank++;
        }

        if (result.OthersBucket is not null)
        {
            AppendParetoRow(sb, sourceId, sourceName, startIso, endIso, axis, numerator, opportunity, weight,
                "OTHERS", result.OthersBucket.GroupKey ?? "OTHERS", result.OthersBucket.GroupName ?? "Others",
                result.OthersBucket);
            await FlushAsync(writer, sb, ct).ConfigureAwait(false);
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private static void AppendParetoRow(
        StringBuilder sb,
        string sourceId, string sourceName, string startIso, string endIso,
        string axis, string numerator, string opportunity, string weight,
        string rank, string? groupKey, string? groupName, ParetoRow row)
    {
        sb.Append(sourceId).Append(',')
          .Append(sourceName).Append(',')
          .Append(startIso).Append(',')
          .Append(endIso).Append(',')
          .Append(axis).Append(',')
          .Append(numerator).Append(',')
          .Append(opportunity).Append(',')
          .Append(weight).Append(',')
          .Append(rank).Append(',')
          .Append(CsvEscape(groupKey)).Append(',')
          .Append(CsvEscape(groupName)).Append(',')
          .Append(row.DefectCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(row.WeightedScore.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.OpportunityCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(row.OpportunitySharePercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.DefectSharePercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.CumulativePercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.IsVitalFew ? "true" : "false").Append("\r\n");
    }

    /// <summary>
    /// <c>GET /api/reports/pareto/export.xlsx</c>. Same query contract
    /// as <see cref="RunParetoAsync"/>. Produces a three-sheet
    /// workbook: <c>Summary</c> holds metadata + the overall KPI,
    /// <c>Applied Filters</c> echoes every narrowing collection so a
    /// consumer can reconstruct the drill breadcrumb, and <c>Rows</c>
    /// holds one typed row per bar (Others row appended at the bottom
    /// when present).
    /// </summary>
    /// <param name="context">Ambient <see cref="HttpContext"/>.</param>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="axis">Group-by axis (kebab-case slug or enum name).</param>
    /// <param name="numerator">Numerator flavour (default <c>real</c>).</param>
    /// <param name="opportunity">Opportunity filter (default <c>all</c>).</param>
    /// <param name="weight">Weight metric (only <c>count</c> ships today).</param>
    /// <param name="topN">Optional cap on visible rows.</param>
    /// <param name="includeOthers">Collapse overflow into a synthetic Others row (default <c>true</c>).</param>
    /// <param name="vitalFewThreshold">Vital-few cumulative-% cut-off (default 80).</param>
    /// <param name="includeObsoleteBits">Include obsolete defect bits when axis=defect.</param>
    /// <param name="machineIds">CSV int list.</param>
    /// <param name="productIds">CSV int list.</param>
    /// <param name="recipeIds">CSV int list.</param>
    /// <param name="defectBits">CSV int list (1..25).</param>
    /// <param name="topologies">CSV string list.</param>
    /// <param name="partNumbers">CSV string list.</param>
    /// <param name="jedecNames">CSV string list.</param>
    /// <param name="sources">All registered AOI sources.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task ExportParetoXlsxAsync(
        HttpContext context,
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
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildParetoRequest(
            sourceId, startUtc, endUtc, axis, numerator, opportunity, weight,
            topN, includeOthers, vitalFewThreshold, includeObsoleteBits,
            machineIds, productIds, recipeIds,
            defectBits, topologies, partNumbers, jedecNames,
            sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunningPareto(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.Axis,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        ParetoResult result;
        try
        {
            result = await ParetoReport.Instance
                .RunAsync(built.Source, built.Filter, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await Results.Problem(
                title: "Invalid Pareto filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        catch (NotSupportedException ex)
        {
            await Results.Problem(
                title: ex.Message,
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"pareto-{built.Source.Descriptor.Id}-{result.Axis}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.xlsx");

        using var buffer = new MemoryStream(16 * 1024);
        BuildParetoWorkbook(result, buffer);
        buffer.Position = 0;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    private static void BuildParetoWorkbook(ParetoResult result, Stream destination)
    {
        using var workbook = new XLWorkbook();

        // ---- Summary sheet ----
        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Nieweb - Pareto";
        summary.Cell("A1").Style.Font.Bold = true;
        summary.Cell("A1").Style.Font.FontSize = 14;
        summary.Range("A1:B1").Merge();

        summary.Cell("A3").Value = "Source Id";
        summary.Cell("B3").Value = result.Source.Id;
        summary.Cell("A4").Value = "Source Name";
        summary.Cell("B4").Value = result.Source.DisplayName;
        summary.Cell("A5").Value = "Window Start (UTC)";
        summary.Cell("B5").Value = result.Window.StartUtc.UtcDateTime;
        summary.Cell("B5").Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        summary.Cell("A6").Value = "Window End (UTC, exclusive)";
        summary.Cell("B6").Value = result.Window.EndUtcExclusive.UtcDateTime;
        summary.Cell("B6").Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        summary.Cell("A7").Value = "Axis";
        summary.Cell("B7").Value = result.Axis.ToString();
        summary.Cell("A8").Value = "Numerator";
        summary.Cell("B8").Value = result.Numerator.ToString();
        summary.Cell("A9").Value = "Opportunity Filter";
        summary.Cell("B9").Value = result.Opportunity.ToString();
        summary.Cell("A10").Value = "Weight";
        summary.Cell("B10").Value = result.Weight.ToString();

        summary.Cell("A12").Value = "Metric";
        summary.Cell("B12").Value = "Value";
        summary.Range("A12:B12").Style.Font.Bold = true;
        summary.Cell("A13").Value = "Tested Objects";
        summary.Cell("B13").Value = result.Overall.TestedObjectCount;
        summary.Cell("A14").Value = "Opportunities";
        summary.Cell("B14").Value = result.Overall.OpportunityCount;
        summary.Cell("A15").Value = "Defect Bits";
        summary.Cell("B15").Value = result.Overall.DefectBitCount;
        summary.Cell("A16").Value = "DPMO (ppm, overall)";
        summary.Cell("B16").Value = result.Overall.DpmoPpm;
        summary.Cell("B16").Style.NumberFormat.Format = "0.####";
        summary.Columns("A:B").AdjustToContents();

        // ---- Applied Filters sheet ----
        var filters = workbook.Worksheets.Add("Applied Filters");
        filters.Cell("A1").Value = "Filter";
        filters.Cell("B1").Value = "Values";
        filters.Range("A1:B1").Style.Font.Bold = true;
        var frow = 2;
        AppendFilterRow(filters, ref frow, "MachineIds", string.Join(",", result.AppliedFilters.MachineIds));
        AppendFilterRow(filters, ref frow, "ProductIds", string.Join(",", result.AppliedFilters.ProductIds));
        AppendFilterRow(filters, ref frow, "RecipeIds", string.Join(",", result.AppliedFilters.RecipeIds));
        AppendFilterRow(filters, ref frow, "DefectBits", string.Join(",", result.AppliedFilters.DefectBits));
        AppendFilterRow(filters, ref frow, "Topologies", string.Join(",", result.AppliedFilters.Topologies));
        AppendFilterRow(filters, ref frow, "PartNumbers", string.Join(",", result.AppliedFilters.PartNumbers));
        AppendFilterRow(filters, ref frow, "JedecNames", string.Join(",", result.AppliedFilters.JedecNames));
        filters.Columns("A:B").AdjustToContents();

        // ---- Rows sheet ----
        var rows = workbook.Worksheets.Add("Rows");
        string[] headers =
        [
            "Rank", "GroupKey", "GroupName",
            "DefectCount", "WeightedScore",
            "OpportunityCount", "OpportunitySharePercent",
            "DpmoPpm", "DefectSharePercent", "CumulativePercent", "IsVitalFew",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            rows.Cell(1, i + 1).Value = headers[i];
        }
        rows.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        var r = 2;
        var rank = 1;
        foreach (var row in result.Rows)
        {
            WriteParetoWorkbookRow(rows, r, rank.ToString(CultureInfo.InvariantCulture),
                row.GroupKey ?? string.Empty, row.GroupName ?? string.Empty, row);
            r++;
            rank++;
        }

        if (result.OthersBucket is not null)
        {
            WriteParetoWorkbookRow(rows, r, "OTHERS",
                result.OthersBucket.GroupKey ?? "OTHERS",
                result.OthersBucket.GroupName ?? "Others",
                result.OthersBucket);
            r++;
        }

        var lastRow = r - 1;
        if (lastRow >= 2)
        {
            rows.Range(1, 1, lastRow, headers.Length).SetAutoFilter();
        }
        rows.Columns(1, headers.Length).AdjustToContents();

        workbook.SaveAs(destination);
    }

    private static void AppendFilterRow(IXLWorksheet sheet, ref int row, string name, string value)
    {
        sheet.Cell(row, 1).Value = name;
        sheet.Cell(row, 2).Value = value;
        row++;
    }

    private static void WriteParetoWorkbookRow(
        IXLWorksheet sheet, int row, string rank, string groupKey, string groupName, ParetoRow data)
    {
        sheet.Cell(row, 1).Value = rank;
        sheet.Cell(row, 2).Value = groupKey;
        sheet.Cell(row, 3).Value = groupName;
        sheet.Cell(row, 4).Value = data.DefectCount;
        sheet.Cell(row, 5).Value = data.WeightedScore;
        sheet.Cell(row, 5).Style.NumberFormat.Format = "0.####";
        sheet.Cell(row, 6).Value = data.OpportunityCount;
        sheet.Cell(row, 7).Value = data.OpportunitySharePercent;
        sheet.Cell(row, 7).Style.NumberFormat.Format = "0.####";
        sheet.Cell(row, 8).Value = data.DpmoPpm;
        sheet.Cell(row, 8).Style.NumberFormat.Format = "0.####";
        sheet.Cell(row, 9).Value = data.DefectSharePercent;
        sheet.Cell(row, 9).Style.NumberFormat.Format = "0.####";
        sheet.Cell(row, 10).Value = data.CumulativePercent;
        sheet.Cell(row, 10).Style.NumberFormat.Format = "0.####";
        sheet.Cell(row, 11).Value = data.IsVitalFew;
    }
}
