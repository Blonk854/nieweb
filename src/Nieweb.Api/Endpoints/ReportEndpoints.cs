using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using ClosedXML.Excel;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for Nieweb reports:
/// <c>GET /api/reports/panel-yield</c> runs
/// <see cref="PanelYieldByLineReport"/> against a named source.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint is a thin HTTP shell over the pure report function -
/// it does not aggregate or format anything itself. This keeps the
/// report reusable (batch jobs, scheduled snapshots, tests) and makes
/// its numeric parity with Vieweb / Sigmalink Analyse a property of
/// the report project, not of the web layer.
/// </para>
/// <para>
/// The endpoint requires authentication. It never writes to the AOI
/// database - <see cref="IAoiSource"/> implementations are read-only
/// per the project-wide read-only discipline.
/// </para>
/// </remarks>
public static partial class ReportEndpoints
{
    /// <summary>
    /// Registers the <c>/api/reports</c> endpoints on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization();

        group.MapGet("/panel-yield", RunPanelYieldAsync)
            .WithName("ReportsPanelYield");

        group.MapGet("/panel-yield/export.csv", ExportPanelYieldCsvAsync)
            .WithName("ReportsPanelYieldExportCsv");

        group.MapGet("/panel-yield/export.xlsx", ExportPanelYieldXlsxAsync)
            .WithName("ReportsPanelYieldExportXlsx");

        group.MapGet("/dpmo-table", RunDpmoTableAsync)
            .WithName("ReportsDpmoTable");

        group.MapGet("/pareto", RunParetoAsync)
            .WithName("ReportsPareto");

        return routes;
    }

    /// <summary>
    /// <c>GET /api/reports/panel-yield</c>.
    /// </summary>
    /// <param name="sourceId">
    /// <see cref="SourceDescriptor.Id"/> of the source to query
    /// (e.g. <c>postreflow</c>, <c>prereflow</c>). Case-insensitive.
    /// </param>
    /// <param name="startUtc">Window start, inclusive. Any parseable ISO-8601 instant.</param>
    /// <param name="endUtc">Window end, exclusive. Must be strictly after <paramref name="startUtc"/>.</param>
    /// <param name="machineIds">Optional comma-separated list of machine ids to include.</param>
    /// <param name="productIds">Optional comma-separated list of product ids to include.</param>
    /// <param name="recipeIds">Optional comma-separated list of recipe ids to include.</param>
    /// <param name="onlyLastInspection">
    /// When <c>true</c> (default), restrict to each panel's latest inspection
    /// on sources that support <see cref="Capabilities.IsLastInspectionFilter"/>.
    /// </param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunPanelYieldAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var built = TryBuildPanelYieldRequest(
            sourceId, startUtc, endUtc, machineIds, productIds, recipeIds,
            onlyLastInspection, sources);
        if (built.Error is not null)
        {
            return built.Error;
        }

        LogRunning(logger, built.Source!.Descriptor.Id, built.Filter!.Window.StartUtc, built.Filter.Window.EndUtcExclusive);
        var result = await PanelYieldByLineReport.Instance
            .RunAsync(built.Source, built.Filter, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>
    /// <c>GET /api/reports/panel-yield/export.csv</c>.
    /// Streams the per-machine breakdown of <see cref="PanelYieldByLineReport"/>
    /// as a UTF-8 (BOM-prefixed) CSV file through the response's
    /// <see cref="PipeWriter"/>, so the response body is never fully
    /// buffered on the server side.
    /// </summary>
    /// <remarks>
    /// The CSV contains one row per machine, with the source id, source
    /// display name, and window bounds repeated on every row so that the
    /// file is self-describing when copy-pasted into Excel or Power Query.
    /// The window end is the exclusive upper bound of the report window,
    /// matching the DateRange semantics used throughout Nieweb.
    /// </remarks>
    private static async Task ExportPanelYieldCsvAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildPanelYieldRequest(
            sourceId, startUtc, endUtc, machineIds, productIds, recipeIds,
            onlyLastInspection, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunning(logger, built.Source!.Descriptor.Id, built.Filter!.Window.StartUtc, built.Filter.Window.EndUtcExclusive);
        var result = await PanelYieldByLineReport.Instance
            .RunAsync(built.Source, built.Filter, cancellationToken)
            .ConfigureAwait(false);

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"panel-yield-{built.Source.Descriptor.Id}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.csv");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/csv; charset=utf-8";
        // RFC 6266 attachment disposition. Filename is ASCII so no filename* needed.
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await WritePanelYieldCsvAsync(context.Response.BodyWriter, result, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes a <see cref="PanelYieldResult"/> as CSV into
    /// <paramref name="writer"/>. UTF-8 BOM is prepended so Excel on
    /// Windows opens the file with the correct encoding.
    /// </summary>
    private static async Task WritePanelYieldCsvAsync(
        PipeWriter writer, PanelYieldResult result, CancellationToken ct)
    {
        // UTF-8 BOM: helps Excel-on-Windows autodetect encoding.
        await writer.WriteAsync(Utf8Bom, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.Append("SourceId,SourceName,WindowStartUtc,WindowEndUtc,MachineId,MachineName,")
          .Append("TotalPanels,InspectedPanels,GoodPanels,FaultyPanels,NotInspectedPanels,FpyPercent\r\n");
        await FlushAsync(writer, sb, ct).ConfigureAwait(false);

        var sourceId = CsvEscape(result.Source.Id);
        var sourceName = CsvEscape(result.Source.DisplayName);
        var startIso = result.Window.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var endIso = result.Window.EndUtcExclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        foreach (var row in result.ByMachine)
        {
            sb.Append(sourceId).Append(',')
              .Append(sourceName).Append(',')
              .Append(startIso).Append(',')
              .Append(endIso).Append(',')
              .Append(row.MachineId.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(CsvEscape(row.MachineName)).Append(',')
              .Append(row.Kpi.TotalPanels.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(row.Kpi.InspectedPanels.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(row.Kpi.GoodPanels.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(row.Kpi.FaultyPanels.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(row.Kpi.NotInspectedPanels.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(row.Kpi.FpyPercent.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
            await FlushAsync(writer, sb, ct).ConfigureAwait(false);
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>Encodes and writes <paramref name="sb"/> as UTF-8, then clears the builder.</summary>
    private static async ValueTask FlushAsync(PipeWriter writer, StringBuilder sb, CancellationToken ct)
    {
        if (sb.Length == 0)
        {
            return;
        }
        var text = sb.ToString();
        sb.Clear();
        var byteCount = Encoding.UTF8.GetByteCount(text);
        var memory = writer.GetMemory(byteCount);
        var written = Encoding.UTF8.GetBytes(text, memory.Span);
        writer.Advance(written);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET /api/reports/panel-yield/export.xlsx</c>.
    /// Same shape as the CSV export but as an Excel 2007+ (.xlsx) workbook
    /// authored with ClosedXML. Contains two sheets:
    /// <list type="bullet">
    ///   <item><description><c>Summary</c> - source / window metadata and the overall KPI row.</description></item>
    ///   <item><description><c>By Machine</c> - one row per machine with typed numeric cells so pivot tables and
    ///     Excel formulas work without a re-parse step.</description></item>
    /// </list>
    /// The workbook is written to a pooled <see cref="MemoryStream"/> then
    /// copied to the response - ClosedXML does not support forward-only
    /// streaming, and Open-XML packages are ZIP archives so they cannot be
    /// serialized as a pure forward-only stream anyway.
    /// </summary>
    private static async Task ExportPanelYieldXlsxAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildPanelYieldRequest(
            sourceId, startUtc, endUtc, machineIds, productIds, recipeIds,
            onlyLastInspection, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunning(logger, built.Source!.Descriptor.Id, built.Filter!.Window.StartUtc, built.Filter.Window.EndUtcExclusive);
        var result = await PanelYieldByLineReport.Instance
            .RunAsync(built.Source, built.Filter, cancellationToken)
            .ConfigureAwait(false);

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"panel-yield-{built.Source.Descriptor.Id}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.xlsx");

        // ClosedXML must fully materialize the workbook before we can hand
        // it off - Open-XML packages are ZIP archives. We buffer into a
        // MemoryStream then stream the bytes to the response.
        using var buffer = new MemoryStream(16 * 1024);
        BuildPanelYieldWorkbook(result, buffer);
        buffer.Position = 0;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the panel-yield workbook (Summary + By Machine sheets) into <paramref name="destination"/>.</summary>
    private static void BuildPanelYieldWorkbook(PanelYieldResult result, Stream destination)
    {
        using var workbook = new XLWorkbook();

        // ---- Summary sheet ----
        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Nieweb - Panel Yield by Line";
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

        summary.Cell("A8").Value = "Metric";
        summary.Cell("B8").Value = "Value";
        summary.Range("A8:B8").Style.Font.Bold = true;
        summary.Cell("A9").Value = "Total Panels";
        summary.Cell("B9").Value = result.Overall.TotalPanels;
        summary.Cell("A10").Value = "Inspected Panels";
        summary.Cell("B10").Value = result.Overall.InspectedPanels;
        summary.Cell("A11").Value = "Good Panels";
        summary.Cell("B11").Value = result.Overall.GoodPanels;
        summary.Cell("A12").Value = "Faulty Panels";
        summary.Cell("B12").Value = result.Overall.FaultyPanels;
        summary.Cell("A13").Value = "Not-Inspected Panels";
        summary.Cell("B13").Value = result.Overall.NotInspectedPanels;
        summary.Cell("A14").Value = "FPY (%)";
        summary.Cell("B14").Value = result.Overall.FpyPercent;
        summary.Cell("B14").Style.NumberFormat.Format = "0.####";
        summary.Columns("A:B").AdjustToContents();

        // ---- By Machine sheet ----
        var by = workbook.Worksheets.Add("By Machine");
        string[] headers =
        [
            "MachineId", "MachineName",
            "TotalPanels", "InspectedPanels", "GoodPanels",
            "FaultyPanels", "NotInspectedPanels", "FpyPercent",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            by.Cell(1, i + 1).Value = headers[i];
        }
        by.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        var row = 2;
        foreach (var m in result.ByMachine)
        {
            by.Cell(row, 1).Value = m.MachineId;
            by.Cell(row, 2).Value = m.MachineName ?? string.Empty;
            by.Cell(row, 3).Value = m.Kpi.TotalPanels;
            by.Cell(row, 4).Value = m.Kpi.InspectedPanels;
            by.Cell(row, 5).Value = m.Kpi.GoodPanels;
            by.Cell(row, 6).Value = m.Kpi.FaultyPanels;
            by.Cell(row, 7).Value = m.Kpi.NotInspectedPanels;
            by.Cell(row, 8).Value = m.Kpi.FpyPercent;
            by.Cell(row, 8).Style.NumberFormat.Format = "0.####";
            row++;
        }

        if (result.ByMachine.Count > 0)
        {
            by.Range(1, 1, result.ByMachine.Count + 1, headers.Length)
              .SetAutoFilter();
        }
        by.Columns(1, headers.Length).AdjustToContents();

        workbook.SaveAs(destination);
    }

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// RFC-4180 CSV field escaping: wrap in double-quotes and double any
    /// embedded double-quote when the value contains a comma, quote, CR,
    /// or LF; otherwise return as-is. <c>null</c> becomes an empty field.
    /// </summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        var needsQuoting =
            value.IndexOfAny(_csvSpecials) >= 0;
        if (!needsQuoting)
        {
            return value;
        }
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static readonly char[] _csvSpecials = [',', '"', '\r', '\n'];
    private static readonly ReadOnlyMemory<byte> Utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };

    /// <summary>
    /// Shared query-string parser used by both the JSON and CSV panel-yield
    /// endpoints. Returns either a resolved source + validated filter, or
    /// the <see cref="IResult"/> to short-circuit the response with.
    /// </summary>
    private static (IAoiSource? Source, PanelYieldFilter? Filter, IResult? Error) TryBuildPanelYieldRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return (null, null, Results.Problem(
                title: "Missing required query parameter 'sourceId'.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var source = sources.FirstOrDefault(s =>
            string.Equals(s.Descriptor.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return (null, null, Results.Problem(
                title: $"Unknown sourceId '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        if (!TryParseUtc(startUtc, out var start))
        {
            return (null, null, Results.Problem(
                title: "Query parameter 'startUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (!TryParseUtc(endUtc, out var end))
        {
            return (null, null, Results.Problem(
                title: "Query parameter 'endUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (end <= start)
        {
            return (null, null, Results.Problem(
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
            return (null, null, Results.Problem(
                title: "Invalid window: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
#pragma warning restore CA1031

        var filter = new PanelYieldFilter(
            Window: window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            RecipeIds: ParseIntList(recipeIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        return (source, filter, null);
    }

    /// <summary>
    /// Parses an ISO-8601 instant and normalizes it to UTC. Accepts both
    /// offset-bearing timestamps (<c>...+00:00</c>) and bare timestamps
    /// (which are assumed to be UTC to match how AOI PANELS timestamps
    /// are already stored).
    /// </summary>
    private static bool TryParseUtc(string? raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            value = parsed.ToUniversalTime();
            return true;
        }
        return false;
    }

    private static List<int>? ParseIntList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                list.Add(id);
            }
        }
        return list.Count == 0 ? null : list;
    }

    /// <summary>
    /// Marker type used only to name the <see cref="ILogger{T}"/> category
    /// for <c>/api/reports</c>.
    /// </summary>
    public sealed class ReportsMarker
    {
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Running panel-yield report on '{SourceId}' for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunning(ILogger logger, string sourceId, DateTimeOffset startUtc, DateTimeOffset endUtc);
}
