using System.Globalization;
using Nieweb.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders an <see cref="FpyTableResult"/> to PDF. Content parity with
/// <c>/api/reports/fpy-table/export.xlsx</c>: metadata + overall KPI up
/// top, then a table ordered by increasing FPY (worst yield first — the
/// report's own emit order, Vieweb §3.1.6.4).
/// </summary>
public static class FpyTablePdfRenderer
{
    /// <summary>Write the PDF into <paramref name="destination"/>.</summary>
    /// <param name="result">Report result to render.</param>
    /// <param name="generatedByDisplayName">User-facing name printed in the footer.</param>
    /// <param name="destination">Target stream.</param>
    /// <param name="generatedAt">Rendering timestamp (defaults to now UTC).</param>
    /// <param name="timeZone">Display time zone (defaults to UTC when null).</param>
    public static void Render(
        FpyTableResult result,
        string generatedByDisplayName,
        Stream destination,
        DateTimeOffset? generatedAt = null,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(generatedByDisplayName);
        ArgumentNullException.ThrowIfNull(destination);

        var tz = timeZone ?? TimeZoneInfo.Utc;
        var subtitle = NiewebPdfTimestamps.FormatSubtitle(
            $"Granularity: {result.Granularity}   Grouped by: {result.GroupBy}   Skip: {result.SkipExclusion}");
        var window = NiewebPdfTimestamps.FormatRange(
            result.Window.StartUtc, result.Window.EndUtcExclusive, tz);

        var doc = new NiewebPdfDocument(
            title: "FPY Table",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, result),
            timeZone: tz,
            footerNote: $"Source: {result.Source.DisplayName}   ·   Window: {window}");

        doc.Render(destination);
    }

    private static void Compose(IContainer body, FpyTableResult result)
    {
        body.Column(col =>
        {
            col.Spacing(10);
            col.Item().Element(c => ComposeOverall(c, result.Overall));
            col.Item().Text($"Rows ({result.Rows.Count})").SemiBold().FontSize(11);
            col.Item().Element(c => ComposeRows(c, result));
        });
    }

    private static void ComposeOverall(IContainer container, FpyKpi kpi)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Row(row =>
        {
            row.RelativeItem().Element(c => KpiCell(c, "Inspected", kpi.InspectedCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Faulty", kpi.FaultyCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "FPY AOI (%)", kpi.FpyAoiPercent.ToString("0.##", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "FPY Diagnostic (%)", kpi.FpyDiagnosticPercent.ToString("0.##", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "FPY After Repair (%)", kpi.FpyAfterRepairPercent.ToString("0.##", CultureInfo.InvariantCulture)));
        });
    }

    private static void KpiCell(IContainer c, string label, string value)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(value).SemiBold().FontSize(12);
        });
    }

    private static void ComposeRows(IContainer container, FpyTableResult result)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.ConstantColumn(70);
                c.ConstantColumn(60);
                c.ConstantColumn(75);
                c.ConstantColumn(85);
                c.ConstantColumn(90);
            });
            PanelYieldPdfRenderer.HeaderCell(t, "Group");
            PanelYieldPdfRenderer.HeaderCell(t, "Inspected");
            PanelYieldPdfRenderer.HeaderCell(t, "Faulty");
            PanelYieldPdfRenderer.HeaderCell(t, "FPY AOI");
            PanelYieldPdfRenderer.HeaderCell(t, "FPY Diag.");
            PanelYieldPdfRenderer.HeaderCell(t, "FPY A/Repair");

            foreach (var row in result.Rows)
            {
                var name = row.GroupName ?? row.GroupKey.ToString(CultureInfo.InvariantCulture);
                PanelYieldPdfRenderer.Cell(t, name);
                PanelYieldPdfRenderer.Cell(t, row.Kpi.InspectedCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.FaultyCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.FpyAoiPercent.ToString("0.##", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.FpyDiagnosticPercent.ToString("0.##", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.FpyAfterRepairPercent.ToString("0.##", CultureInfo.InvariantCulture));
            }
        });
    }
}
