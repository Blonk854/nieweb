using System.Globalization;
using System.IO.Pipelines;
using System.Text;

using ClosedXML.Excel;

using Nieweb.Api.SkipClassification;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// FPY table endpoint (<c>GET /api/reports/fpy-table</c>) wired over
/// <see cref="FpyTableReport"/>. Supports Vieweb's panel / sub-panel
/// granularity and AOI / product grouping, plus the skip-exclusion
/// toggle (<c>raw</c> / <c>clean</c>) that reads FPY on the clean
/// production population, and CSV / XLSX / PDF exports.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/fpy-table</c>. Returns an
    /// <see cref="FpyTableResult"/> for the requested source / window /
    /// granularity / grouping. <c>granularity</c> accepts
    /// <c>panel</c> (default) or <c>board</c>; <c>groupBy</c> accepts
    /// <c>aoi-machine</c> (default) or <c>product</c>;
    /// <c>skipExclusion</c> accepts <c>raw</c> (default) or
    /// <c>clean</c>.
    /// </summary>
    private static async Task<IResult> RunFpyTableAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? granularity,
        string? groupBy,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var built = TryBuildFpyRequest(
            sourceId, startUtc, endUtc, granularity, groupBy,
            machineIds, productIds, onlyLastInspection, skipExclusion, skipStatuses, excludeNogo, sources);
        if (built.Error is not null)
        {
            return built.Error;
        }

        var filter = built.Filter! with
        {
            SkipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false),
        };

        LogRunningFpy(
            logger,
            built.Source!.Descriptor.Id,
            filter.Granularity,
            filter.GroupBy,
            filter.SkipExclusion,
            filter.Window.StartUtc,
            filter.Window.EndUtcExclusive);

        var result = await FpyTableReport.Instance
            .RunAsync(built.Source, filter, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static (IAoiSource? Source, FpyTableFilter? Filter, IResult? Error) TryBuildFpyRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? granularity,
        string? groupBy,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources)
    {
        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return (null, null, baseParse.Error);
        }

        if (!TryParseEnumAlias<FpyGranularity>(granularity, required: false, out var granularityValue, out var error, defaultValue: FpyGranularity.Panel))
        {
            return (null, null, ProblemFor("granularity", error!));
        }
        if (!TryParseEnumAlias<FpyGroupBy>(groupBy, required: false, out var groupByValue, out error, defaultValue: FpyGroupBy.AoiMachine))
        {
            return (null, null, ProblemFor("groupBy", error!));
        }
        if (!TryParseEnumAlias<SkipExclusion>(skipExclusion, required: false, out var skipValue, out error, defaultValue: SkipExclusion.Raw))
        {
            return (null, null, ProblemFor("skipExclusion", error!));
        }

        var filter = new FpyTableFilter(
            Window: baseParse.Window,
            Granularity: granularityValue,
            GroupBy: groupByValue,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true,
            SkipExclusion: skipValue,
            SkipStatuses: ParseSkipClassList(skipStatuses),
            ExcludeNogo: excludeNogo ?? false);

        return (baseParse.Source, filter, null);
    }

    // -------------------------------------------------------------------------
    // Export endpoints (CSV + XLSX; PDF lives in ReportEndpoints.Pdf.cs).
    // -------------------------------------------------------------------------

    private static async Task ExportFpyTableCsvAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? granularity,
        string? groupBy,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await BuildFpyResultAsync(
            context, sourceId, startUtc, endUtc, granularity, groupBy,
            machineIds, productIds, onlyLastInspection, skipExclusion, skipStatuses,
            excludeNogo, sources, skipConfigProvider, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"fpy-{result.Source.Id}-{result.Granularity}-{result.Window.StartUtc:yyyyMMdd}-{result.Window.EndUtcExclusive:yyyyMMdd}.csv");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await WriteFpyCsvAsync(context.Response.BodyWriter, result, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportFpyTableXlsxAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? granularity,
        string? groupBy,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await BuildFpyResultAsync(
            context, sourceId, startUtc, endUtc, granularity, groupBy,
            machineIds, productIds, onlyLastInspection, skipExclusion, skipStatuses,
            excludeNogo, sources, skipConfigProvider, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"fpy-{result.Source.Id}-{result.Granularity}-{result.Window.StartUtc:yyyyMMdd}-{result.Window.EndUtcExclusive:yyyyMMdd}.xlsx");

        using var buffer = new MemoryStream(16 * 1024);
        BuildFpyWorkbook(result, buffer);
        buffer.Position = 0;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared build-and-run for the FPY export endpoints. Writes the 4xx
    /// error to <paramref name="context"/> and returns <c>null</c> when the
    /// request is invalid; otherwise returns the computed result.
    /// </summary>
    private static async Task<FpyTableResult?> BuildFpyResultAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? granularity,
        string? groupBy,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        CancellationToken cancellationToken)
    {
        var built = TryBuildFpyRequest(
            sourceId, startUtc, endUtc, granularity, groupBy,
            machineIds, productIds, onlyLastInspection, skipExclusion, skipStatuses, excludeNogo, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return null;
        }

        var filter = built.Filter! with
        {
            SkipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false),
        };
        return await FpyTableReport.Instance
            .RunAsync(built.Source!, filter, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteFpyCsvAsync(
        PipeWriter writer, FpyTableResult result, CancellationToken ct)
    {
        await writer.WriteAsync(Utf8Bom, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.Append("SourceId,SourceName,WindowStartUtc,WindowEndUtc,Granularity,GroupBy,SkipExclusion,")
          .Append("GroupKey,GroupName,TotalRows,Inspected,NotInspected,Faulty,GoodAoi,GoodDiagnostic,GoodAfterRepair,")
          .Append("FpyAoiPercent,FpyDiagnosticPercent,FpyAfterRepairPercent\r\n");
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        var sourceId = CsvEscape(result.Source.Id);
        var sourceName = CsvEscape(result.Source.DisplayName);
        var startIso = result.Window.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var endIso = result.Window.EndUtcExclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var granularity = result.Granularity.ToString();
        var groupBy = result.GroupBy.ToString();
        var skip = result.SkipExclusion.ToString();

        AppendFpyRow(sb, sourceId, sourceName, startIso, endIso, granularity, groupBy, skip,
            "OVERALL", "Overall", result.Overall);
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        foreach (var row in result.Rows)
        {
            AppendFpyRow(sb, sourceId, sourceName, startIso, endIso, granularity, groupBy, skip,
                row.GroupKey.ToString(CultureInfo.InvariantCulture), row.GroupName, row.Kpi);
            await FlushAsync(writer, sb, ct).ConfigureAwait(false);
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private static void AppendFpyRow(
        StringBuilder sb,
        string sourceId, string sourceName, string startIso, string endIso,
        string granularity, string groupBy, string skip,
        string? groupKey, string? groupName, FpyKpi kpi)
    {
        sb.Append(sourceId).Append(',')
          .Append(sourceName).Append(',')
          .Append(startIso).Append(',')
          .Append(endIso).Append(',')
          .Append(granularity).Append(',')
          .Append(groupBy).Append(',')
          .Append(skip).Append(',')
          .Append(CsvEscape(groupKey)).Append(',')
          .Append(CsvEscape(groupName)).Append(',')
          .Append(kpi.TotalRows.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.InspectedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.NotInspectedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.FaultyCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.GoodAoiCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.GoodDiagnosticCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.GoodAfterRepairCount.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.FpyAoiPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.FpyDiagnosticPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
          .Append(kpi.FpyAfterRepairPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
    }

    private static void BuildFpyWorkbook(FpyTableResult result, Stream destination)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Nieweb - FPY Table";
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
        summary.Cell("A7").Value = "Granularity";
        summary.Cell("B7").Value = result.Granularity.ToString();
        summary.Cell("A8").Value = "Group By";
        summary.Cell("B8").Value = result.GroupBy.ToString();
        summary.Cell("A9").Value = "Skip Exclusion";
        summary.Cell("B9").Value = result.SkipExclusion.ToString();

        summary.Cell("A11").Value = "Metric";
        summary.Cell("B11").Value = "Value";
        summary.Range("A11:B11").Style.Font.Bold = true;
        summary.Cell("A12").Value = "Inspected";
        summary.Cell("B12").Value = result.Overall.InspectedCount;
        summary.Cell("A13").Value = "Faulty";
        summary.Cell("B13").Value = result.Overall.FaultyCount;
        summary.Cell("A14").Value = "FPY AOI (%)";
        summary.Cell("B14").Value = result.Overall.FpyAoiPercent;
        summary.Cell("B14").Style.NumberFormat.Format = "0.##";
        summary.Cell("A15").Value = "FPY Diagnostic (%)";
        summary.Cell("B15").Value = result.Overall.FpyDiagnosticPercent;
        summary.Cell("B15").Style.NumberFormat.Format = "0.##";
        summary.Cell("A16").Value = "FPY After Repair (%)";
        summary.Cell("B16").Value = result.Overall.FpyAfterRepairPercent;
        summary.Cell("B16").Style.NumberFormat.Format = "0.##";
        summary.Columns("A:B").AdjustToContents();

        var rows = workbook.Worksheets.Add("Rows");
        string[] headers =
        [
            "Group Key", "Group Name", "Total", "Inspected", "Not Inspected", "Faulty",
            "Good AOI", "Good Diagnostic", "Good After Repair",
            "FPY AOI (%)", "FPY Diagnostic (%)", "FPY After Repair (%)",
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            rows.Cell(1, c + 1).Value = headers[c];
        }
        rows.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in result.Rows)
        {
            rows.Cell(r, 1).Value = row.GroupKey;
            rows.Cell(r, 2).Value = row.GroupName ?? string.Empty;
            rows.Cell(r, 3).Value = row.Kpi.TotalRows;
            rows.Cell(r, 4).Value = row.Kpi.InspectedCount;
            rows.Cell(r, 5).Value = row.Kpi.NotInspectedCount;
            rows.Cell(r, 6).Value = row.Kpi.FaultyCount;
            rows.Cell(r, 7).Value = row.Kpi.GoodAoiCount;
            rows.Cell(r, 8).Value = row.Kpi.GoodDiagnosticCount;
            rows.Cell(r, 9).Value = row.Kpi.GoodAfterRepairCount;
            rows.Cell(r, 10).Value = row.Kpi.FpyAoiPercent;
            rows.Cell(r, 10).Style.NumberFormat.Format = "0.##";
            rows.Cell(r, 11).Value = row.Kpi.FpyDiagnosticPercent;
            rows.Cell(r, 11).Style.NumberFormat.Format = "0.##";
            rows.Cell(r, 12).Value = row.Kpi.FpyAfterRepairPercent;
            rows.Cell(r, 12).Style.NumberFormat.Format = "0.##";
            r++;
        }
        rows.Columns(1, headers.Length).AdjustToContents();

        workbook.SaveAs(destination);
    }


    [LoggerMessage(EventId = 3005, Level = LogLevel.Information,
        Message = "Running fpy-table on '{SourceId}' granularity={Granularity} groupBy={GroupBy} skip={SkipExclusion} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningFpy(
        ILogger logger,
        string sourceId,
        FpyGranularity granularity,
        FpyGroupBy groupBy,
        SkipExclusion skipExclusion,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);
}
