using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;

using Nieweb.Api.Reports;
using Nieweb.DataSources;
using Nieweb.Pdf;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Multi-tile "report" export endpoints (docs/phase-2.md §7.6 <c>RC5</c>).
/// Given a saved <see cref="Data.Entities.Report"/>, iterate its tiles
/// in <c>DisplayOrder</c> and emit either a single XLSX (one worksheet
/// per tile plus a cover) or a single PDF (one section per tile plus
/// a cover). Filters are shared across every tile and passed via the
/// query string (there is no per-tile stored config today).
/// </summary>
/// <remarks>
/// Only tile types actually shipping in the SPA canvas are supported
/// today (<c>panelYield</c>, <c>pareto</c>, <c>comment</c>). Unknown
/// tile types render a placeholder row / section so users see which
/// tile did not render rather than losing the whole download.
/// </remarks>
public static partial class ReportEndpoints
{
    /// <summary>
    /// Registers the <c>/{id}/export.xlsx</c> and <c>/{id}/export.pdf</c>
    /// endpoints on <paramref name="group"/>. Called from
    /// <see cref="MapReportEndpoints(IEndpointRouteBuilder)"/>.
    /// </summary>
    private static void MapReportExportEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{id:int}/export.xlsx", ExportReportXlsxAsync)
             .WithName("ReportsReportExportXlsx");

        group.MapGet("/{id:int}/export.pdf", ExportReportPdfAsync)
             .WithName("ReportsReportExportPdf");
    }

    // -------------------- XLSX --------------------

    private static async Task ExportReportXlsxAsync(
        HttpContext context,
        int id,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        IReports reports,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reports);

        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            await Results.NotFound().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var built = TryBuildPanelYieldRequest(
            sourceId, startUtc, endUtc, machineIds, productIds,
            onlyLastInspection, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var sections = await RunTileSectionsAsync(
            detail, built.Source!, built.Filter!, machineIds, productIds,
            logger, cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream(32 * 1024);
        BuildReportWorkbook(detail, built.Source!, built.Filter!, sections, buffer);
        buffer.Position = 0;

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"report-{detail.Report.Id}-{built.Source!.Descriptor.Id}-{built.Filter!.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.xlsx");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = XlsxContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    // -------------------- PDF --------------------

    private static async Task ExportReportPdfAsync(
        HttpContext context,
        int id,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        IReports reports,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reports);

        var detail = await reports.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            await Results.NotFound().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var built = TryBuildPanelYieldRequest(
            sourceId, startUtc, endUtc, machineIds, productIds,
            onlyLastInspection, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var sections = await RunTileSectionsAsync(
            detail, built.Source!, built.Filter!, machineIds, productIds,
            logger, cancellationToken).ConfigureAwait(false);

        var header = new ReportPdfRenderer.ReportHeader(
            ReportTitle: detail.Report.Title,
            Description: detail.Report.Description,
            SourceId: built.Source!.Descriptor.Id,
            SourceDisplayName: built.Source.Descriptor.DisplayName,
            WindowStartUtc: built.Filter!.Window.StartUtc,
            WindowEndExclusiveUtc: built.Filter.Window.EndUtcExclusive);

        using var buffer = new MemoryStream(32 * 1024);
        ReportPdfRenderer.Render(header, sections, ResolveDisplayName(context.User), buffer);
        buffer.Position = 0;

        var filename = string.Create(CultureInfo.InvariantCulture,
            $"report-{detail.Report.Id}-{built.Source.Descriptor.Id}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}.pdf");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = PdfContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    // -------------------- Shared: tile fan-out --------------------

    /// <summary>
    /// Runs each tile in <paramref name="detail"/> against
    /// <paramref name="filter"/>, returning one <see cref="ReportPdfRenderer.TileSection"/>
    /// per tile. Unknown tile types return a section with
    /// <see cref="ReportPdfRenderer.TileSection.Result"/> set to
    /// <c>null</c> so downstream renderers can emit a placeholder.
    /// </summary>
    private static async Task<IReadOnlyList<ReportPdfRenderer.TileSection>> RunTileSectionsAsync(
        ReportDetail detail,
        IAoiSource source,
        PanelYieldFilter filter,
        string? machineIds,
        string? productIds,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var sections = new List<ReportPdfRenderer.TileSection>(detail.Entities.Count);
        foreach (var tile in detail.Entities)
        {
            var title = string.IsNullOrWhiteSpace(tile.Title)
                ? tile.TileType
                : tile.Title!;

            object? result = tile.TileType switch
            {
                "panelYield" => await RunPanelYieldForTileAsync(source, filter, logger, cancellationToken).ConfigureAwait(false),
                "pareto"     => await RunParetoForTileAsync(source, filter, machineIds, productIds, logger, cancellationToken).ConfigureAwait(false),
                "comment"    => ExtractCommentResult(tile.ConfigJson),
                _            => null,
            };
            sections.Add(new ReportPdfRenderer.TileSection(
                Title: title,
                TileType: tile.TileType,
                Result: result));
        }
        return sections;
    }

    private static async Task<PanelYieldResult> RunPanelYieldForTileAsync(
        IAoiSource source,
        PanelYieldFilter filter,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        LogRunning(logger, source.Descriptor.Id, filter.Window.StartUtc, filter.Window.EndUtcExclusive);
        return await PanelYieldByLineReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ParetoResult> RunParetoForTileAsync(
        IAoiSource source,
        PanelYieldFilter shared,
        string? machineIds,
        string? productIds,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        // For the multi-tile export we use the same default Pareto
        // shape the SPA canvas tile does: axis=Defect (per-defect-bit
        // bars), Real numerator, All-object opportunity, Count-weighted
        // score, no top-N trimming so users see the full histogram
        // side-by-side with the panel-yield tile.
        var filter = new ParetoFilter(
            Window: shared.Window,
            Axis: ParetoAxis.Defect,
            Numerator: DpmoNumerator.Real,
            Opportunity: DpmoOpportunity.All,
            Weight: ParetoWeight.Count,
            TopN: null,
            IncludeOthersBucket: true,
            VitalFewThresholdPercent: 80.0,
            IncludeObsoleteBits: false,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            DefectBits: null,
            Topologies: null,
            PartNumbers: null,
            JedecNames: null,
            SiteTimeZone: null,
            Shifts: null);

        LogRunningPareto(
            logger, source.Descriptor.Id, filter.Axis, filter.Numerator,
            filter.Window.StartUtc, filter.Window.EndUtcExclusive);
        return await ParetoReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Parses a comment tile's <c>ConfigJson</c> and returns a
    /// <see cref="ReportPdfRenderer.CommentTileResult"/>. Malformed
    /// JSON, missing <c>markdown</c> field, and null / empty values
    /// all resolve to a result with <c>Markdown = null</c> so the
    /// export still runs (the renderer / writer will show an
    /// "(empty comment)" placeholder). The property is looked up
    /// case-insensitively to match the SPA's JSON conventions.
    /// </summary>
    private static ReportPdfRenderer.CommentTileResult ExtractCommentResult(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new ReportPdfRenderer.CommentTileResult(null);
        }
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ReportPdfRenderer.CommentTileResult(null);
            }
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.NameEquals("markdown") && !string.Equals(prop.Name, "Markdown", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    return new ReportPdfRenderer.CommentTileResult(prop.Value.GetString());
                }
                break;
            }
            return new ReportPdfRenderer.CommentTileResult(null);
        }
        catch (JsonException)
        {
            return new ReportPdfRenderer.CommentTileResult(null);
        }
    }

    // -------------------- XLSX workbook --------------------

    /// <summary>
    /// Builds a workbook with a "Cover" sheet describing the report
    /// and one sheet per tile. Sheet names are prefixed with the
    /// 1-based tile index so ordering is preserved and duplicate
    /// tile titles cannot collide (Excel worksheet names must be
    /// unique within a workbook).
    /// </summary>
    private static void BuildReportWorkbook(
        ReportDetail detail,
        IAoiSource source,
        PanelYieldFilter filter,
        IReadOnlyList<ReportPdfRenderer.TileSection> sections,
        Stream destination)
    {
        using var workbook = new XLWorkbook();

        var cover = workbook.Worksheets.Add("Cover");
        cover.Cell("A1").Value = "Nieweb - " + detail.Report.Title;
        cover.Cell("A1").Style.Font.Bold = true;
        cover.Cell("A1").Style.Font.FontSize = 14;
        cover.Range("A1:B1").Merge();

        cover.Cell("A3").Value = "Report Id";
        cover.Cell("B3").Value = detail.Report.Id;
        cover.Cell("A4").Value = "Owner";
        cover.Cell("B4").Value = detail.Report.OwnerDisplayName;
        cover.Cell("A5").Value = "Source Id";
        cover.Cell("B5").Value = source.Descriptor.Id;
        cover.Cell("A6").Value = "Source Name";
        cover.Cell("B6").Value = source.Descriptor.DisplayName;
        cover.Cell("A7").Value = "Window Start (UTC)";
        cover.Cell("B7").Value = filter.Window.StartUtc.UtcDateTime;
        cover.Cell("B7").Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        cover.Cell("A8").Value = "Window End (UTC, exclusive)";
        cover.Cell("B8").Value = filter.Window.EndUtcExclusive.UtcDateTime;
        cover.Cell("B8").Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        if (!string.IsNullOrWhiteSpace(detail.Report.Description))
        {
            cover.Cell("A9").Value = "Description";
            cover.Cell("B9").Value = detail.Report.Description;
        }

        cover.Cell("A11").Value = "#";
        cover.Cell("B11").Value = "Tile title";
        cover.Cell("C11").Value = "Tile type";
        cover.Cell("D11").Value = "Status";
        cover.Range("A11:D11").Style.Font.Bold = true;

        for (var i = 0; i < sections.Count; i++)
        {
            var row = 12 + i;
            var section = sections[i];
            cover.Cell(row, 1).Value = i + 1;
            cover.Cell(row, 2).Value = section.Title;
            cover.Cell(row, 3).Value = section.TileType;
            cover.Cell(row, 4).Value = section.Result is null ? "unsupported (skipped)" : "rendered";
        }
        cover.Columns("A:D").AdjustToContents();

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var sheetName = BuildSheetName(i + 1, section);
            var sheet = workbook.Worksheets.Add(sheetName);
            sheet.Cell("A1").Value = section.Title;
            sheet.Cell("A1").Style.Font.Bold = true;
            sheet.Cell("A1").Style.Font.FontSize = 12;

            switch (section.Result)
            {
                case PanelYieldResult panelYield:
                    WritePanelYieldSheet(sheet, panelYield);
                    break;
                case ParetoResult pareto:
                    WriteParetoSheet(sheet, pareto);
                    break;
                case ReportPdfRenderer.CommentTileResult comment:
                    WriteCommentSheet(sheet, comment);
                    break;
                default:
                    sheet.Cell("A3").Value = "Unsupported tile type '" + section.TileType + "' - please open a Nieweb issue.";
                    sheet.Cell("A3").Style.Font.Italic = true;
                    break;
            }
            sheet.Columns().AdjustToContents();
        }

        workbook.SaveAs(destination);
    }

    /// <summary>
    /// Excel sheet names are limited to 31 characters and cannot
    /// contain <c>: / \ ? * [ ]</c>. This helper produces
    /// <c>"NN. Title"</c>, sanitised and truncated.
    /// </summary>
    private static string BuildSheetName(int index, ReportPdfRenderer.TileSection section)
    {
        var raw = string.Create(CultureInfo.InvariantCulture,
            $"{index:00}. {section.Title}");
        // Strip Excel-reserved characters.
        Span<char> sanitised = stackalloc char[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            sanitised[i] = c is ':' or '/' or '\\' or '?' or '*' or '[' or ']' ? '-' : c;
        }
        var trimmed = new string(sanitised);
        return trimmed.Length <= 31 ? trimmed : trimmed[..31];
    }

    private static void WritePanelYieldSheet(IXLWorksheet sheet, PanelYieldResult result)
    {
        sheet.Cell("A3").Value = "Metric";
        sheet.Cell("B3").Value = "Value";
        sheet.Range("A3:B3").Style.Font.Bold = true;
        sheet.Cell("A4").Value = "Total Panels";
        sheet.Cell("B4").Value = result.Overall.TotalPanels;
        sheet.Cell("A5").Value = "Inspected Panels";
        sheet.Cell("B5").Value = result.Overall.InspectedPanels;
        sheet.Cell("A6").Value = "Good Panels";
        sheet.Cell("B6").Value = result.Overall.GoodPanels;
        sheet.Cell("A7").Value = "Faulty Panels";
        sheet.Cell("B7").Value = result.Overall.FaultyPanels;
        sheet.Cell("A8").Value = "Not-Inspected Panels";
        sheet.Cell("B8").Value = result.Overall.NotInspectedPanels;
        sheet.Cell("A9").Value = "FPY (%)";
        sheet.Cell("B9").Value = result.Overall.FpyPercent;
        sheet.Cell("B9").Style.NumberFormat.Format = "0.####";

        sheet.Cell("A11").Value = "MachineId";
        sheet.Cell("B11").Value = "MachineName";
        sheet.Cell("C11").Value = "Total";
        sheet.Cell("D11").Value = "Inspected";
        sheet.Cell("E11").Value = "Good";
        sheet.Cell("F11").Value = "Faulty";
        sheet.Cell("G11").Value = "NotInspected";
        sheet.Cell("H11").Value = "FpyPercent";
        sheet.Range("A11:H11").Style.Font.Bold = true;
        var r = 12;
        foreach (var m in result.ByMachine)
        {
            sheet.Cell(r, 1).Value = m.MachineId;
            sheet.Cell(r, 2).Value = m.MachineName ?? string.Empty;
            sheet.Cell(r, 3).Value = m.Kpi.TotalPanels;
            sheet.Cell(r, 4).Value = m.Kpi.InspectedPanels;
            sheet.Cell(r, 5).Value = m.Kpi.GoodPanels;
            sheet.Cell(r, 6).Value = m.Kpi.FaultyPanels;
            sheet.Cell(r, 7).Value = m.Kpi.NotInspectedPanels;
            sheet.Cell(r, 8).Value = m.Kpi.FpyPercent;
            sheet.Cell(r, 8).Style.NumberFormat.Format = "0.####";
            r++;
        }
    }

    private static void WriteParetoSheet(IXLWorksheet sheet, ParetoResult result)
    {
        sheet.Cell("A3").Value = "Axis";
        sheet.Cell("B3").Value = result.Axis.ToString();
        sheet.Cell("A4").Value = "Numerator";
        sheet.Cell("B4").Value = result.Numerator.ToString();
        sheet.Cell("A5").Value = "Weight";
        sheet.Cell("B5").Value = result.Weight.ToString();
        sheet.Cell("A6").Value = "Defect bits";
        sheet.Cell("B6").Value = result.Overall.DefectBitCount;
        sheet.Cell("A7").Value = "Opportunities";
        sheet.Cell("B7").Value = result.Overall.OpportunityCount;
        sheet.Cell("A8").Value = "DPMO (ppm)";
        sheet.Cell("B8").Value = result.Overall.DpmoPpm;
        sheet.Cell("B8").Style.NumberFormat.Format = "0.####";

        sheet.Cell("A10").Value = "GroupKey";
        sheet.Cell("B10").Value = "GroupName";
        sheet.Cell("C10").Value = "DefectCount";
        sheet.Cell("D10").Value = "DpmoPpm";
        sheet.Cell("E10").Value = "SharePercent";
        sheet.Cell("F10").Value = "CumulativePercent";
        sheet.Range("A10:F10").Style.Font.Bold = true;
        var r = 11;
        foreach (var row in result.Rows)
        {
            sheet.Cell(r, 1).Value = row.GroupKey ?? string.Empty;
            sheet.Cell(r, 2).Value = row.GroupName ?? string.Empty;
            sheet.Cell(r, 3).Value = row.DefectCount;
            sheet.Cell(r, 4).Value = row.DpmoPpm;
            sheet.Cell(r, 4).Style.NumberFormat.Format = "0.####";
            sheet.Cell(r, 5).Value = row.DefectSharePercent;
            sheet.Cell(r, 5).Style.NumberFormat.Format = "0.####";
            sheet.Cell(r, 6).Value = row.CumulativePercent;
            sheet.Cell(r, 6).Style.NumberFormat.Format = "0.####";
            r++;
        }
        if (result.OthersBucket is { } others)
        {
            sheet.Cell(r, 1).Value = others.GroupKey ?? "others";
            sheet.Cell(r, 2).Value = others.GroupName ?? "Others";
            sheet.Cell(r, 3).Value = others.DefectCount;
            sheet.Cell(r, 4).Value = others.DpmoPpm;
            sheet.Cell(r, 4).Style.NumberFormat.Format = "0.####";
            sheet.Cell(r, 5).Value = others.DefectSharePercent;
            sheet.Cell(r, 5).Style.NumberFormat.Format = "0.####";
            sheet.Cell(r, 6).Value = others.CumulativePercent;
            sheet.Cell(r, 6).Style.NumberFormat.Format = "0.####";
        }
    }

    /// <summary>
    /// Writes a comment tile (docs/phase-2.md §7.6 <c>RC6</c>) to a
    /// worksheet. The raw markdown is placed in <c>A3</c> with
    /// <c>WrapText</c> enabled and the row height set so long
    /// comments stay readable without manual re-sizing. Empty
    /// markdown falls back to a dimmed placeholder so the reader
    /// can tell an intentional blank apart from a rendering bug.
    /// </summary>
    private static void WriteCommentSheet(IXLWorksheet sheet, ReportPdfRenderer.CommentTileResult comment)
    {
        var text = comment.Markdown;
        if (string.IsNullOrWhiteSpace(text))
        {
            sheet.Cell("A3").Value = "(empty comment)";
            sheet.Cell("A3").Style.Font.Italic = true;
            return;
        }
        sheet.Cell("A3").Value = text;
        sheet.Cell("A3").Style.Alignment.WrapText = true;
        sheet.Cell("A3").Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        // A comfortably readable width for a markdown paragraph without
        // fighting AdjustToContents in <see cref="BuildReportWorkbook"/>.
        sheet.Column("A").Width = 80;
    }
}
