using System.Globalization;
using System.IO.Pipelines;
using System.Text;

using ClosedXML.Excel;

using Nieweb.Api.Reports;
using Nieweb.Api.SkipClassification;
using Nieweb.DataSources;
using Nieweb.Reports;
using Nieweb.Reports.Common;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// DPMO trend-by-line endpoint (<c>GET /api/reports/dpmo-trend</c>) wired over
/// <see cref="DpmoTrendByLineReport"/>. Like <c>/api/reports/fpy-trend</c> it
/// runs across <em>every</em> registered source and returns one
/// <see cref="DpmoTrendResult"/> per source, because machine ids do not
/// correspond across the pre-/post-reflow databases. Per-source failures are
/// isolated: an offline / mis-configured DB is omitted rather than failing
/// the whole page.
/// </summary>
/// <remarks>
/// The <c>numerator</c> toggle (AOI / Real / Dummy) is deliberately absent
/// from the query contract: every cell already carries all three, so the
/// client switches between them without a refetch. The <c>opportunity</c>
/// toggle IS a query parameter, because it changes both the denominator and
/// which objects contribute defects.
/// </remarks>
public static partial class ReportEndpoints
{
    /// <summary>Top-level response: the shared toggles plus one result per source.</summary>
    public sealed record DpmoTrendReportResponse(
        TimeBucket Bucket,
        DpmoOpportunity Opportunity,
        SkipExclusion SkipExclusion,
        IReadOnlyList<DpmoTrendResult> Sources);

    /// <summary>
    /// <c>GET /api/reports/dpmo-trend</c>. Runs the per-line DPMO trend across
    /// every source (or the subset named by <paramref name="sourceIds"/>).
    /// <c>bucket</c> accepts <c>day</c> or <c>week</c>; <c>opportunity</c>
    /// accepts <c>all</c>, <c>components</c> (default) or <c>paste</c>;
    /// <c>skipExclusion</c> accepts <c>raw</c> or <c>clean</c> (default
    /// <c>clean</c>).
    /// </summary>
    private static async Task<IResult> RunDpmoTrendAsync(
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? opportunity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var (response, _, error) = await BuildDpmoTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, opportunity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, excludeNogo,
            sources, skipConfigProvider, resultCache, useCache: false,
            logger, cancellationToken).ConfigureAwait(false);
        return error ?? Results.Ok(response);
    }

    /// <summary>
    /// Parses + validates the query, then runs <see cref="DpmoTrendByLineReport"/>
    /// against every selected source. Returns the assembled response (and the
    /// parsed window, for export filenames), or a 4xx <see cref="IResult"/>.
    /// <para>
    /// <c>useCache</c> is <c>false</c> for the on-screen report (always runs
    /// fresh, and stores its per-source results) and <c>true</c> for the CSV /
    /// XLSX / PDF exports, which reuse that stored pass when it is still live.
    /// Keeps a view plus its three exports at a single AOI pass (TR4).
    /// </para>
    /// </summary>
    private static async Task<(DpmoTrendReportResponse? Response, DateRange Window, IResult? Error)> BuildDpmoTrendAsync(
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? opportunity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        bool useCache,
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
        if (!TryParseEnumAlias<DpmoOpportunity>(opportunity, required: false, out var opportunityValue, out enumError, defaultValue: DpmoOpportunity.Components))
        {
            return (null, window, ProblemFor("opportunity", enumError!));
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
        var baseFilter = new DpmoTrendFilter(
            Window: window,
            Bucket: bucketValue,
            SiteTimeZone: siteTz,
            Opportunity: opportunityValue,
            MachineIds: null,
            ProductIds: ParseIntList(productIds),
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
        // any selected line contributes nothing (rather than everything — an
        // empty MachineIds list reads as "no filter" further down the stack).
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

            LogRunningDpmoTrend(logger, source.Descriptor.Id, bucketValue, opportunityValue, window.StartUtc, window.EndUtcExclusive);
            try
            {
                if (useCache)
                {
                    return await resultCache
                        .GetOrRunAsync(DpmoTrendByLineReport.Instance, source, filter, cancellationToken)
                        .ConfigureAwait(false);
                }
                var perSource = await DpmoTrendByLineReport.Instance
                    .RunAsync(source, filter, cancellationToken).ConfigureAwait(false);
                resultCache.Store(DpmoTrendByLineReport.Instance, source, filter, perSource);
                return perSource;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // per-source isolation: an offline DB must not fail the whole page
            catch (Exception ex)
            {
                LogDpmoTrendSourceFailed(logger, source.Descriptor.Id, ex);
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
        var response = new DpmoTrendReportResponse(bucketValue, opportunityValue, skipValue, ordered);
        return (response, window, null);
    }

    // -------------------------------------------------------------------------
    // Exports (CSV + XLSX). One row per (source, line, bucket). Every row
    // carries the shared opportunity count plus all three numerators and all
    // three DPMO rates, so a spreadsheet user gets the same toggle the SPA has.
    // -------------------------------------------------------------------------

    private static async Task ExportDpmoTrendCsvAsync(
        HttpContext context,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? opportunity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (response, window, error) = await BuildDpmoTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, opportunity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, excludeNogo,
            sources, skipConfigProvider, resultCache, useCache: true,
            logger, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            await error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var filename = DpmoTrendFilename(response!, window, "csv");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await WriteDpmoTrendCsvAsync(context.Response.BodyWriter, response!, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportDpmoTrendXlsxAsync(
        HttpContext context,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? opportunity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (response, window, error) = await BuildDpmoTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, opportunity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, excludeNogo,
            sources, skipConfigProvider, resultCache, useCache: true,
            logger, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            await error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var filename = DpmoTrendFilename(response!, window, "xlsx");
        using var buffer = new MemoryStream(16 * 1024);
        BuildDpmoTrendWorkbook(response!, buffer);
        buffer.Position = 0;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    private static string DpmoTrendFilename(DpmoTrendReportResponse response, DateRange window, string ext) =>
        string.Create(CultureInfo.InvariantCulture,
            $"dpmo-trend-{response.Bucket}-{response.Opportunity}-{window.StartUtc:yyyyMMdd}-{window.EndUtcExclusive:yyyyMMdd}.{ext}")
            .ToLowerInvariant();

    private static async Task WriteDpmoTrendCsvAsync(
        PipeWriter writer, DpmoTrendReportResponse response, CancellationToken ct)
    {
        await writer.WriteAsync(Utf8Bom, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.Append("SourceId,SourceName,Opportunity,SkipExclusion,MachineId,MachineName,")
          .Append("BucketIndex,BucketLabel,BucketStartUtc,BucketEndUtc,")
          .Append("Opportunities,DefectsAoi,DefectsReal,DefectsDummy,")
          .Append("DpmoAoi,DpmoReal,DpmoDummy\r\n");
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        var opportunity = response.Opportunity.ToString();
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
                      .Append(opportunity).Append(',')
                      .Append(skip).Append(',')
                      .Append(line.MachineId.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(machineName).Append(',')
                      .Append(point.BucketIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(CsvEscape(b.Label)).Append(',')
                      .Append(b.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',')
                      .Append(b.EndUtcExclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.OpportunityCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.DefectsAoi.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.DefectsReal.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.DefectsDummy.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.DpmoAoi.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.DpmoReal.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                      .Append(kpi.DpmoDummy.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
                    await FlushAsync(writer, sb, ct).ConfigureAwait(false);
                }
            }
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private static void BuildDpmoTrendWorkbook(DpmoTrendReportResponse response, Stream destination)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Nieweb - DPMO Trend";
        summary.Cell("A1").Style.Font.Bold = true;
        summary.Cell("A1").Style.Font.FontSize = 14;
        summary.Range("A1:B1").Merge();
        summary.Cell("A3").Value = "Bucket";
        summary.Cell("B3").Value = response.Bucket.ToString();
        summary.Cell("A4").Value = "Opportunity";
        summary.Cell("B4").Value = response.Opportunity.ToString();
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
            "Opportunities", "Defects AOI", "Defects Real", "Defects Dummy",
            "DPMO AOI", "DPMO Real", "DPMO Dummy",
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
                    data.Cell(r, 8).Value = kpi.OpportunityCount;
                    data.Cell(r, 9).Value = kpi.DefectsAoi;
                    data.Cell(r, 10).Value = kpi.DefectsReal;
                    data.Cell(r, 11).Value = kpi.DefectsDummy;
                    data.Cell(r, 12).Value = kpi.DpmoAoi;
                    data.Cell(r, 12).Style.NumberFormat.Format = "0.##";
                    data.Cell(r, 13).Value = kpi.DpmoReal;
                    data.Cell(r, 13).Style.NumberFormat.Format = "0.##";
                    data.Cell(r, 14).Value = kpi.DpmoDummy;
                    data.Cell(r, 14).Style.NumberFormat.Format = "0.##";
                    r++;
                }
            }
        }
        data.Columns(1, headers.Length).AdjustToContents();

        workbook.SaveAs(destination);
    }

    [LoggerMessage(EventId = 3420, Level = LogLevel.Information,
        Message = "Running DPMO trend on '{SourceId}' bucket={Bucket} opportunity={Opportunity} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningDpmoTrend(
        ILogger logger,
        string sourceId,
        TimeBucket bucket,
        DpmoOpportunity opportunity,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);

    [LoggerMessage(EventId = 3421, Level = LogLevel.Warning,
        Message = "DPMO trend failed for source '{SourceId}'; omitting it from the response")]
    private static partial void LogDpmoTrendSourceFailed(ILogger logger, string sourceId, Exception ex);
}
