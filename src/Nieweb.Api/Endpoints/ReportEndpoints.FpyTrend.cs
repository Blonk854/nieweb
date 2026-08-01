using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.RegularExpressions;

using ClosedXML.Excel;

using Nieweb.Api.SkipClassification;
using Nieweb.DataSources;
using Nieweb.Reports;
using Nieweb.Reports.Common;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// FPY trend-by-line endpoint (<c>GET /api/reports/fpy-trend</c>) wired over
/// <see cref="FpyTrendByLineReport"/>. Unlike the other report endpoints it
/// runs across <em>every</em> registered source (machine ids collide across
/// pre- / post-reflow, so each line stays namespaced by its source) and
/// returns one <see cref="FpyTrendResult"/> per source. Per-source failures
/// are isolated the same way <c>/api/sources</c> isolates freshness probes:
/// an offline / mis-configured DB is omitted rather than failing the page.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>Top-level response: the shared toggles plus one result per source.</summary>
    public sealed record FpyTrendReportResponse(
        TimeBucket Bucket,
        FpyGranularity Granularity,
        SkipExclusion SkipExclusion,
        IReadOnlyList<FpyTrendResult> Sources);

    /// <summary>
    /// <c>GET /api/reports/fpy-trend</c>. Runs the per-line FPY trend across
    /// every source (or the subset named by <paramref name="sourceIds"/>).
    /// <c>bucket</c> accepts <c>day</c> or <c>week</c>; <c>granularity</c>
    /// accepts <c>panel</c> or <c>board</c> (default <c>board</c> =
    /// sub-panel); <c>skipExclusion</c> accepts <c>raw</c> or <c>clean</c>
    /// (default <c>clean</c>).
    /// </summary>
    private static async Task<IResult> RunFpyTrendAsync(
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? granularity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? onlyLastInspection,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var (response, _, error) = await BuildFpyTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, granularity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, onlyLastInspection, excludeNogo,
            sources, skipConfigProvider, logger, cancellationToken).ConfigureAwait(false);
        return error ?? Results.Ok(response);
    }

    /// <summary>
    /// Parses + validates the query, then runs <see cref="FpyTrendByLineReport"/>
    /// against every selected source. Returns the assembled response (and the
    /// parsed window, for export filenames), or a 4xx <see cref="IResult"/>.
    /// </summary>
    private static async Task<(FpyTrendReportResponse? Response, DateRange Window, IResult? Error)> BuildFpyTrendAsync(
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? granularity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? onlyLastInspection,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(skipConfigProvider);

        if (!TryParseUtc(startUtc, out var start))
        {
            return (null, default, CodedProblem(
                ProblemCodes.InvalidStart,
                "Query parameter 'startUtc' is missing or not a valid ISO-8601 UTC instant."));
        }
        if (!TryParseUtc(endUtc, out var end))
        {
            return (null, default, CodedProblem(
                ProblemCodes.InvalidEnd,
                "Query parameter 'endUtc' is missing or not a valid ISO-8601 UTC instant."));
        }
        if (end <= start)
        {
            return (null, default, CodedProblem(
                ProblemCodes.EmptyWindow,
                "'endUtc' must be strictly after 'startUtc'."));
        }
        DateRange window;
        try
        {
            window = new DateRange(start, end);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return (null, default, CodedProblem(ProblemCodes.InvalidWindow, "Invalid window: " + ex.Message));
        }

        if (!TryParseEnumAlias<TimeBucket>(bucket, required: true, out var bucketValue, out var enumError))
        {
            return (null, window, ProblemFor("bucket", enumError!));
        }
        if (bucketValue is not (TimeBucket.Day or TimeBucket.Week))
        {
            return (null, window, ProblemFor("bucket", "only 'day' or 'week' are supported."));
        }
        if (!TryParseEnumAlias<FpyGranularity>(granularity, required: false, out var granularityValue, out enumError, defaultValue: FpyGranularity.Board))
        {
            return (null, window, ProblemFor("granularity", enumError!));
        }
        if (!TryParseEnumAlias<SkipExclusion>(skipExclusion, required: false, out var skipValue, out enumError, defaultValue: SkipExclusion.Clean))
        {
            return (null, window, ProblemFor("skipExclusion", enumError!));
        }

        var siteTz = TryParseTimeZone(siteTimeZone, out var tzError);
        if (tzError is not null)
        {
            return (null, window, ProblemFor("siteTimeZone", tzError));
        }

        var wantedIds = ParseStringList(sourceIds);
        var selected = wantedIds is null
            ? sources.ToList()
            : sources
                .Where(s => wantedIds.Any(id => string.Equals(id, s.Descriptor.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        var requestedLines = ParseIntList(lines) is { Count: > 0 } ls
            ? new HashSet<int>(ls)
            : null;

        var skipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        var baseFilter = new FpyTrendFilter(
            Window: window,
            Bucket: bucketValue,
            SiteTimeZone: siteTz,
            Granularity: granularityValue,
            MachineIds: null,
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true,
            SkipExclusion: skipValue,
            SkipConfig: skipConfig,
            SkipStatuses: ParseSkipClassList(skipStatuses),
            ExcludeNogo: excludeNogo ?? false);

        // Run the sources concurrently. They are distinct databases
        // (post- vs pre-reflow), so parallel runs add no per-DB load and
        // roughly halve wall-clock time. Per-source failures stay isolated.
        //
        // The "Line" filter is by *line number* (parsed from the machine name,
        // e.g. L2PSTAOI -> line 2), NOT by machine id: machine ids do not
        // correspond across the pre-/post-reflow DBs. Each source resolves the
        // requested lines to ITS OWN machine ids; a source with no machine on
        // any selected line contributes nothing (rather than everything).
        var tasks = selected.Select(async source =>
        {
            List<int>? machineIds = null;
            if (requestedLines is not null)
            {
                var catalogue = await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false);
                machineIds = catalogue
                    .Where(m => TryParseLineNumber(m.MachineName, out var ln) && requestedLines.Contains(ln))
                    .Select(m => m.MachineId)
                    .ToList();
                if (machineIds.Count == 0)
                {
                    return null;
                }
            }
            var filter = baseFilter with { MachineIds = machineIds };

            LogRunningFpyTrend(logger, source.Descriptor.Id, bucketValue, granularityValue, window.StartUtc, window.EndUtcExclusive);
            try
            {
                return await FpyTrendByLineReport.Instance
                    .RunAsync(source, filter, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // per-source isolation: an offline DB must not fail the whole page
            catch (Exception ex)
            {
                LogFpyTrendSourceFailed(logger, source.Descriptor.Id, ex);
                return null;
            }
#pragma warning restore CA1031
        }).ToList();

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);

        var ordered = completed
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderBy(r => r.Source.Id, StringComparer.Ordinal)
            .ToList();
        var response = new FpyTrendReportResponse(bucketValue, granularityValue, skipValue, ordered);
        return (response, window, null);
    }

    /// <summary>
    /// Extracts the physical line number from an AOI machine name. Machine
    /// names encode the line as a leading <c>L{n}</c> (e.g. <c>L2PSTAOI</c> =
    /// line 2 post-reflow, <c>L7PREAOI</c> = line 7 pre-reflow). Returns
    /// <c>false</c> for names that do not follow the convention.
    /// </summary>
    internal static bool TryParseLineNumber(string? machineName, out int line)
    {
        line = 0;
        if (string.IsNullOrWhiteSpace(machineName))
        {
            return false;
        }
        var match = LineNumberRegex.Match(machineName.Trim());
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out line);
    }

    private static readonly Regex LineNumberRegex =
        new(@"^L(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // -------------------------------------------------------------------------
    // Exports (CSV + XLSX). One row per (source, line, bucket).
    // -------------------------------------------------------------------------

    private static async Task ExportFpyTrendCsvAsync(
        HttpContext context,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? granularity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? onlyLastInspection,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (response, window, error) = await BuildFpyTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, granularity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, onlyLastInspection, excludeNogo,
            sources, skipConfigProvider, logger, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            await error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var filename = FpyTrendFilename(response!, window, "csv");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await WriteFpyTrendCsvAsync(context.Response.BodyWriter, response!, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportFpyTrendXlsxAsync(
        HttpContext context,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? granularity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? onlyLastInspection,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (response, window, error) = await BuildFpyTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, granularity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, onlyLastInspection, excludeNogo,
            sources, skipConfigProvider, logger, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            await error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var filename = FpyTrendFilename(response!, window, "xlsx");
        using var buffer = new MemoryStream(16 * 1024);
        BuildFpyTrendWorkbook(response!, buffer);
        buffer.Position = 0;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    private static string FpyTrendFilename(FpyTrendReportResponse response, DateRange window, string ext) =>
        string.Create(CultureInfo.InvariantCulture,
            $"fpy-trend-{response.Bucket}-{response.Granularity}-{window.StartUtc:yyyyMMdd}-{window.EndUtcExclusive:yyyyMMdd}.{ext}")
            .ToLowerInvariant();

    private static async Task WriteFpyTrendCsvAsync(
        PipeWriter writer, FpyTrendReportResponse response, CancellationToken ct)
    {
        await writer.WriteAsync(Utf8Bom, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.Append("SourceId,SourceName,Granularity,SkipExclusion,MachineId,MachineName,")
          .Append("BucketIndex,BucketLabel,BucketStartUtc,BucketEndUtc,")
          .Append("Inspected,Faulty,NotInspected,GoodAoi,GoodDiagnostic,GoodAfterRepair,")
          .Append("FpyAoiPercent,FpyDiagnosticPercent,FpyAfterRepairPercent\r\n");
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        var granularity = response.Granularity.ToString();
        var skip = response.SkipExclusion.ToString();

        foreach (var source in response.Sources)
        {
            var sourceId = CsvEscape(source.Source.Id);
            var sourceName = CsvEscape(source.Source.DisplayName);
            foreach (var line in source.Lines)
            {
                var machineName = CsvEscape(line.MachineName);
                foreach (var point in line.Points)
                {
                    var b = source.Buckets[point.BucketIndex];
                    var kpi = point.Kpi;
                    sb.Append(sourceId).Append(',')
                      .Append(sourceName).Append(',')
                      .Append(granularity).Append(',')
                      .Append(skip).Append(',')
                      .Append(line.MachineId.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(machineName).Append(',')
                      .Append(point.BucketIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(CsvEscape(b.Label)).Append(',')
                      .Append(b.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',')
                      .Append(b.EndUtcExclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.InspectedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.FaultyCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.NotInspectedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.GoodAoiCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.GoodDiagnosticCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.GoodAfterRepairCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.FpyAoiPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.FpyDiagnosticPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.FpyAfterRepairPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
                    await FlushAsync(writer, sb, ct).ConfigureAwait(false);
                }
            }
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private static void BuildFpyTrendWorkbook(FpyTrendReportResponse response, Stream destination)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Nieweb - FPY Trend";
        summary.Cell("A1").Style.Font.Bold = true;
        summary.Cell("A1").Style.Font.FontSize = 14;
        summary.Range("A1:B1").Merge();
        summary.Cell("A3").Value = "Bucket";
        summary.Cell("B3").Value = response.Bucket.ToString();
        summary.Cell("A4").Value = "Granularity";
        summary.Cell("B4").Value = response.Granularity.ToString();
        summary.Cell("A5").Value = "Skip Exclusion";
        summary.Cell("B5").Value = response.SkipExclusion.ToString();
        summary.Cell("A6").Value = "Sources";
        summary.Cell("B6").Value = response.Sources.Count;
        summary.Columns("A:B").AdjustToContents();

        var data = workbook.Worksheets.Add("Data");
        string[] headers =
        [
            "Source Id", "Source Name", "Machine Id", "Machine Name",
            "Bucket Index", "Bucket Label", "Bucket Start (UTC)",
            "Inspected", "Faulty", "Not Inspected",
            "Good AOI", "Good Diagnostic", "Good After Repair",
            "FPY AOI (%)", "FPY Diagnostic (%)", "FPY After Repair (%)",
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            data.Cell(1, c + 1).Value = headers[c];
        }
        data.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        var r = 2;
        foreach (var source in response.Sources)
        {
            foreach (var line in source.Lines)
            {
                foreach (var point in line.Points)
                {
                    var b = source.Buckets[point.BucketIndex];
                    var kpi = point.Kpi;
                    data.Cell(r, 1).Value = source.Source.Id;
                    data.Cell(r, 2).Value = source.Source.DisplayName;
                    data.Cell(r, 3).Value = line.MachineId;
                    data.Cell(r, 4).Value = line.MachineName ?? string.Empty;
                    data.Cell(r, 5).Value = point.BucketIndex;
                    data.Cell(r, 6).Value = b.Label;
                    data.Cell(r, 7).Value = b.StartUtc.UtcDateTime;
                    data.Cell(r, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    data.Cell(r, 8).Value = kpi.InspectedCount;
                    data.Cell(r, 9).Value = kpi.FaultyCount;
                    data.Cell(r, 10).Value = kpi.NotInspectedCount;
                    data.Cell(r, 11).Value = kpi.GoodAoiCount;
                    data.Cell(r, 12).Value = kpi.GoodDiagnosticCount;
                    data.Cell(r, 13).Value = kpi.GoodAfterRepairCount;
                    data.Cell(r, 14).Value = kpi.FpyAoiPercent;
                    data.Cell(r, 14).Style.NumberFormat.Format = "0.##";
                    data.Cell(r, 15).Value = kpi.FpyDiagnosticPercent;
                    data.Cell(r, 15).Style.NumberFormat.Format = "0.##";
                    data.Cell(r, 16).Value = kpi.FpyAfterRepairPercent;
                    data.Cell(r, 16).Style.NumberFormat.Format = "0.##";
                    r++;
                }
            }
        }
        data.Columns(1, headers.Length).AdjustToContents();

        workbook.SaveAs(destination);
    }

    [LoggerMessage(EventId = 3410, Level = LogLevel.Information,
        Message = "Running FPY trend on '{SourceId}' bucket={Bucket} granularity={Granularity} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningFpyTrend(
        ILogger logger,
        string sourceId,
        TimeBucket bucket,
        FpyGranularity granularity,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);

    [LoggerMessage(EventId = 3411, Level = LogLevel.Warning,
        Message = "FPY trend failed for source '{SourceId}'; omitting it from the response")]
    private static partial void LogFpyTrendSourceFailed(ILogger logger, string sourceId, Exception ex);
}
