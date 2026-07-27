using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using ClosedXML.Excel;
using Nieweb.Api.SkipClassification;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// DPMO table endpoint (<c>GET /api/reports/dpmo-table</c>) wired
/// over <see cref="DpmoTableReport"/>.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/dpmo-table</c>. Returns a
    /// <see cref="DpmoTableResult"/> for the requested source / window
    /// / group-by axis. Supports Vieweb's three numerators (AOI /
    /// Real / Dummy) and three opportunity filters (All / Components
    /// / Paste), and every group-by axis from Vieweb §3.1.6.5.
    /// </summary>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="groupBy">
    /// Group-by axis. Accepts either the kebab-case slug
    /// (<c>aoi-machine</c>, <c>defect</c>, <c>product</c>,
    /// <c>reference-designator</c>, <c>part-number</c>, <c>jedec</c>)
    /// or the raw <see cref="DpmoGroupBy"/> member name.
    /// </param>
    /// <param name="numerator">
    /// One of <c>real</c> (default), <c>aoi</c>, or <c>dummy</c>.
    /// </param>
    /// <param name="opportunity">
    /// One of <c>all</c> (default), <c>components</c>, or <c>paste</c>.
    /// </param>
    /// <param name="machineIds">Optional comma-separated int list.</param>
    /// <param name="productIds">Optional comma-separated int list.</param>
    /// <param name="includeObsoleteBits">
    /// When <c>true</c> and <see cref="DpmoGroupBy.Defect"/>, emit rows
    /// for defect bits flagged obsolete in the catalogue. Default <c>false</c>.
    /// </param>
    /// <param name="skipExclusion">Skip handling: <c>raw</c> (default) or <c>clean</c> to exclude skipped / empty boards.</param>
    /// <param name="skipStatuses">Optional comma-separated SkipClass names; keeps only boards whose class is in the set.</param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="skipConfigProvider">Resolves the admin-tuned skip thresholds.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunDpmoTableAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        bool? includeObsoleteBits,
        string? skipExclusion,
        string? skipStatuses,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var built = TryBuildDpmoRequest(
            sourceId, startUtc, endUtc, groupBy, numerator, opportunity,
            machineIds, productIds, includeObsoleteBits, skipExclusion, skipStatuses, sources);
        if (built.Error is not null)
        {
            return built.Error;
        }

        LogRunningDpmo(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.GroupBy,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        var effectiveFilter = built.Filter! with
        {
            SkipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false),
        };
        var result = await DpmoTableReport.Instance
            .RunAsync(built.Source, effectiveFilter, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static (IAoiSource? Source, DpmoTableFilter? Filter, IResult? Error) TryBuildDpmoRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        bool? includeObsoleteBits,
        string? skipExclusion,
        string? skipStatuses,
        IEnumerable<IAoiSource> sources)
    {
        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return (null, null, baseParse.Error);
        }

        if (!TryParseEnumAlias<DpmoGroupBy>(groupBy, required: true, out var groupByValue, out var error))
        {
            return (null, null, ProblemFor("groupBy", error!));
        }
        if (!TryParseEnumAlias<DpmoNumerator>(numerator, required: false, out var numeratorValue, out error, defaultValue: DpmoNumerator.Real))
        {
            return (null, null, ProblemFor("numerator", error!));
        }
        if (!TryParseEnumAlias<DpmoOpportunity>(opportunity, required: false, out var opportunityValue, out error, defaultValue: DpmoOpportunity.All))
        {
            return (null, null, ProblemFor("opportunity", error!));
        }
        if (!TryParseEnumAlias<SkipExclusion>(skipExclusion, required: false, out var skipValue, out error, defaultValue: SkipExclusion.Raw))
        {
            return (null, null, ProblemFor("skipExclusion", error!));
        }

        var filter = new DpmoTableFilter(
            Window: baseParse.Window,
            GroupBy: groupByValue,
            Numerator: numeratorValue,
            Opportunity: opportunityValue,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            IncludeObsoleteBits: includeObsoleteBits ?? false,
            SkipExclusion: skipValue,
            SkipStatuses: ParseSkipClassList(skipStatuses));

        return (baseParse.Source, filter, null);
    }

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Running dpmo-table on '{SourceId}' groupBy={GroupBy} numerator={Numerator} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningDpmo(
        ILogger logger,
        string sourceId,
        DpmoGroupBy groupBy,
        DpmoNumerator numerator,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);

    /// <summary>
    /// Case-insensitive enum parser that also strips dashes so
    /// kebab-case URL parameters (e.g. <c>reference-designator</c>)
    /// match the underlying PascalCase enum member names.
    /// Returns <c>false</c> and populates <paramref name="error"/>
    /// with a client-safe message when the input is invalid.
    /// </summary>
    private static bool TryParseEnumAlias<TEnum>(
        string? raw,
        bool required,
        out TEnum value,
        out string? error,
        TEnum defaultValue = default)
        where TEnum : struct, Enum
    {
        value = defaultValue;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (required)
            {
                error = "value is required.";
                return false;
            }
            return true;
        }
        // Strip dashes and underscores so kebab / snake case match
        // the CLR PascalCase member names (Enum.TryParse is
        // case-insensitive already).
        var normalized = raw.Replace("-", string.Empty, StringComparison.Ordinal)
                            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }
        error = $"'{raw}' is not a valid {typeof(TEnum).Name}. Allowed values: {string.Join(", ", Enum.GetNames<TEnum>())}.";
        return false;
    }

    private static IResult ProblemFor(string field, string detail) =>
        Results.Problem(
            title: $"Query parameter '{field}' is invalid: {detail}",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Shared source-id + window parser used by every non-panel-yield
    /// endpoint (DPMO, Pareto, and any future reports that consume
    /// the same three query parameters). Returns either
    /// (<paramref name="sources"/>-resolved source + validated window)
    /// or a ready-to-return 4xx <see cref="IResult"/>.
    /// </summary>
    private static (IAoiSource? Source, DateRange Window, IResult? Error) TryBuildBaseRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        IEnumerable<IAoiSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return (null, default, Results.Problem(
                title: "Missing required query parameter 'sourceId'.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        var source = sources.FirstOrDefault(s =>
            string.Equals(s.Descriptor.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return (null, default, Results.Problem(
                title: $"Unknown sourceId '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound));
        }
        if (!TryParseUtc(startUtc, out var start))
        {
            return (null, default, Results.Problem(
                title: "Query parameter 'startUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (!TryParseUtc(endUtc, out var end))
        {
            return (null, default, Results.Problem(
                title: "Query parameter 'endUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (end <= start)
        {
            return (null, default, Results.Problem(
                title: "'endUtc' must be strictly after 'startUtc'.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        DateRange window;
        try
        {
            window = new DateRange(start, end);
        }
#pragma warning disable CA1031 // catch general exception - report a client-friendly 400 for any DateRange rejection
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return (null, default, Results.Problem(
                title: "Invalid window: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
#pragma warning restore CA1031
        return (source, window, null);
    }

    private static List<string>? ParseStringList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : new List<string>(parts);
    }

    // ReSharper disable once UnusedMember.Local - kept for future percent parsing.
    private static bool TryParsePercent(string? raw, out double value, out string? error)
    {
        value = 0d;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            error = $"'{raw}' is not a valid decimal number.";
            return false;
        }
        if (value < 0 || value > 100)
        {
            error = $"must be between 0 and 100 (got {value.ToString(CultureInfo.InvariantCulture)}).";
            return false;
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Export endpoints
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>GET /api/reports/dpmo-table/export.csv</c>. Same query
    /// contract as <see cref="RunDpmoTableAsync"/>. Streams a UTF-8
    /// (BOM-prefixed) CSV with one header row, one <c>OVERALL</c>
    /// summary row, then one row per group bucket. Every row repeats
    /// the source id, source name, window bounds, and axis metadata
    /// so the file is self-describing.
    /// </summary>
    /// <param name="context">Ambient <see cref="HttpContext"/>.</param>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="groupBy">Group-by axis (kebab-case slug or enum name).</param>
    /// <param name="numerator">Numerator flavour (default <c>real</c>).</param>
    /// <param name="opportunity">Opportunity filter (default <c>all</c>).</param>
    /// <param name="machineIds">Optional comma-separated int list.</param>
    /// <param name="productIds">Optional comma-separated int list.</param>
    /// <param name="includeObsoleteBits">Include obsolete defect bits when grouping by defect.</param>
    /// <param name="skipExclusion">Skip handling: <c>raw</c> (default) or <c>clean</c>.</param>
    /// <param name="skipStatuses">Optional comma-separated SkipClass names; keeps only boards whose class is in the set.</param>
    /// <param name="sources">All registered AOI sources.</param>
    /// <param name="skipConfigProvider">Resolves the admin-tuned skip thresholds.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task ExportDpmoTableCsvAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        bool? includeObsoleteBits,
        string? skipExclusion,
        string? skipStatuses,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildDpmoRequest(
            sourceId, startUtc, endUtc, groupBy, numerator, opportunity,
            machineIds, productIds, includeObsoleteBits, skipExclusion, skipStatuses, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunningDpmo(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.GroupBy,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        var effectiveFilter = built.Filter! with
        {
            SkipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false),
        };
        var result = await DpmoTableReport.Instance
            .RunAsync(built.Source, effectiveFilter, cancellationToken)
            .ConfigureAwait(false);

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"dpmo-{built.Source.Descriptor.Id}-{result.GroupBy}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.csv");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await WriteDpmoCsvAsync(context.Response.BodyWriter, result, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteDpmoCsvAsync(
        PipeWriter writer, DpmoTableResult result, CancellationToken ct)
    {
        await writer.WriteAsync(Utf8Bom, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.Append("SourceId,SourceName,WindowStartUtc,WindowEndUtc,GroupBy,Numerator,Opportunity,")
          .Append("GroupKey,GroupName,TestedObjectCount,OpportunityCount,DefectBitCount,DpmoPpm\r\n");
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        var sourceId = CsvEscape(result.Source.Id);
        var sourceName = CsvEscape(result.Source.DisplayName);
        var startIso = result.Window.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var endIso = result.Window.EndUtcExclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var groupBy = result.GroupBy.ToString();
        var numerator = result.Numerator.ToString();
        var opportunity = result.Opportunity.ToString();

        AppendDpmoRow(sb, sourceId, sourceName, startIso, endIso, groupBy, numerator, opportunity,
            "OVERALL", "Overall", result.Overall);
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        foreach (var row in result.Rows)
        {
            AppendDpmoRow(sb, sourceId, sourceName, startIso, endIso, groupBy, numerator, opportunity,
                row.GroupKey, row.GroupName, row.Kpi);
            await FlushAsync(writer, sb, ct).ConfigureAwait(false);
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private static void AppendDpmoRow(
        StringBuilder sb,
        string sourceId, string sourceName, string startIso, string endIso,
        string groupBy, string numerator, string opportunity,
        string? groupKey, string? groupName, DpmoKpi kpi)
    {
        sb.Append(sourceId).Append(',')
          .Append(sourceName).Append(',')
          .Append(startIso).Append(',')
          .Append(endIso).Append(',')
          .Append(groupBy).Append(',')
          .Append(numerator).Append(',')
          .Append(opportunity).Append(',')
          .Append(CsvEscape(groupKey)).Append(',')
          .Append(CsvEscape(groupName)).Append(',')
          .Append(kpi.TestedObjectCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.OpportunityCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.DefectBitCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
    }

    /// <summary>
    /// <c>GET /api/reports/dpmo-table/export.xlsx</c>. Same query
    /// contract as <see cref="RunDpmoTableAsync"/>. Produces a
    /// two-sheet workbook: <c>Summary</c> holds the metadata + overall
    /// KPI, <c>Rows</c> holds one typed row per group bucket sorted
    /// descending by DPMO.
    /// </summary>
    /// <param name="context">Ambient <see cref="HttpContext"/>.</param>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="groupBy">Group-by axis (kebab-case slug or enum name).</param>
    /// <param name="numerator">Numerator flavour (default <c>real</c>).</param>
    /// <param name="opportunity">Opportunity filter (default <c>all</c>).</param>
    /// <param name="machineIds">Optional comma-separated int list.</param>
    /// <param name="productIds">Optional comma-separated int list.</param>
    /// <param name="includeObsoleteBits">Include obsolete defect bits when grouping by defect.</param>
    /// <param name="skipExclusion">Skip handling: <c>raw</c> (default) or <c>clean</c>.</param>
    /// <param name="skipStatuses">Optional comma-separated SkipClass names; keeps only boards whose class is in the set.</param>
    /// <param name="sources">All registered AOI sources.</param>
    /// <param name="skipConfigProvider">Resolves the admin-tuned skip thresholds.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task ExportDpmoTableXlsxAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        bool? includeObsoleteBits,
        string? skipExclusion,
        string? skipStatuses,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildDpmoRequest(
            sourceId, startUtc, endUtc, groupBy, numerator, opportunity,
            machineIds, productIds, includeObsoleteBits, skipExclusion, skipStatuses, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunningDpmo(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.GroupBy,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        var effectiveFilter = built.Filter! with
        {
            SkipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false),
        };
        var result = await DpmoTableReport.Instance
            .RunAsync(built.Source, effectiveFilter, cancellationToken)
            .ConfigureAwait(false);

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"dpmo-{built.Source.Descriptor.Id}-{result.GroupBy}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.xlsx");

        using var buffer = new MemoryStream(16 * 1024);
        BuildDpmoWorkbook(result, buffer);
        buffer.Position = 0;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    private static void BuildDpmoWorkbook(DpmoTableResult result, Stream destination)
    {
        using var workbook = new XLWorkbook();

        // ---- Summary sheet ----
        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Nieweb - DPMO Table";
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
        summary.Cell("A7").Value = "Group By";
        summary.Cell("B7").Value = result.GroupBy.ToString();
        summary.Cell("A8").Value = "Numerator";
        summary.Cell("B8").Value = result.Numerator.ToString();
        summary.Cell("A9").Value = "Opportunity Filter";
        summary.Cell("B9").Value = result.Opportunity.ToString();

        summary.Cell("A11").Value = "Metric";
        summary.Cell("B11").Value = "Value";
        summary.Range("A11:B11").Style.Font.Bold = true;
        summary.Cell("A12").Value = "Tested Objects";
        summary.Cell("B12").Value = result.Overall.TestedObjectCount;
        summary.Cell("A13").Value = "Opportunities";
        summary.Cell("B13").Value = result.Overall.OpportunityCount;
        summary.Cell("A14").Value = "Defect Bits";
        summary.Cell("B14").Value = result.Overall.DefectBitCount;
        summary.Cell("A15").Value = "DPMO (ppm)";
        summary.Cell("B15").Value = result.Overall.DpmoPpm;
        summary.Cell("B15").Style.NumberFormat.Format = "0.####";
        summary.Columns("A:B").AdjustToContents();

        // ---- Rows sheet ----
        var rows = workbook.Worksheets.Add("Rows");
        string[] headers =
        [
            "GroupKey", "GroupName",
            "TestedObjectCount", "OpportunityCount", "DefectBitCount", "DpmoPpm",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            rows.Cell(1, i + 1).Value = headers[i];
        }
        rows.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in result.Rows)
        {
            rows.Cell(r, 1).Value = row.GroupKey ?? string.Empty;
            rows.Cell(r, 2).Value = row.GroupName ?? string.Empty;
            rows.Cell(r, 3).Value = row.Kpi.TestedObjectCount;
            rows.Cell(r, 4).Value = row.Kpi.OpportunityCount;
            rows.Cell(r, 5).Value = row.Kpi.DefectBitCount;
            rows.Cell(r, 6).Value = row.Kpi.DpmoPpm;
            rows.Cell(r, 6).Style.NumberFormat.Format = "0.####";
            r++;
        }

        if (result.Rows.Count > 0)
        {
            rows.Range(1, 1, result.Rows.Count + 1, headers.Length).SetAutoFilter();
        }
        rows.Columns(1, headers.Length).AdjustToContents();

        workbook.SaveAs(destination);
    }
}
