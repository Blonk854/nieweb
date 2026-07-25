using System.Globalization;
using Nieweb.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a <see cref="DpmoTableResult"/> to PDF. Content parity
/// with <c>/api/reports/dpmo-table/export.xlsx</c>: metadata + overall
/// KPI up top, then a table sorted worst-DPMO-first (matches the
/// report's own emit order — see docs/phase-2.md §7.2 TR2).
/// </summary>
public static class DpmoTablePdfRenderer
{
    /// <summary>Write the PDF into <paramref name="destination"/>.</summary>
    /// <param name="result">Report result to render.</param>
    /// <param name="generatedByDisplayName">User-facing name printed in the footer.</param>
    /// <param name="destination">Target stream.</param>
    /// <param name="generatedAt">Rendering timestamp (defaults to now UTC).</param>
    /// <param name="timeZone">Display time zone (defaults to UTC when null).</param>
    public static void Render(
        DpmoTableResult result,
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
            $"Grouped by: {result.GroupBy}   Numerator: {result.Numerator}   Opportunity: {result.Opportunity}",
            $"Window: {NiewebPdfTimestamps.FormatRange(result.Window.StartUtc, result.Window.EndUtcExclusive, tz)}");

        var doc = new NiewebPdfDocument(
            title: "DPMO Table",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, result),
            timeZone: tz,
            footerNote: $"Source: {result.Source.DisplayName}");

        doc.Render(destination);
    }

    private static void Compose(IContainer body, DpmoTableResult result)
    {
        body.Column(col =>
        {
            col.Spacing(10);
            col.Item().Element(c => ComposeOverall(c, result.Overall));
            col.Item().Text($"Rows ({result.Rows.Count})").SemiBold().FontSize(11);
            col.Item().Element(c => ComposeRows(c, result));
        });
    }

    private static void ComposeOverall(IContainer container, DpmoKpi kpi)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Row(row =>
        {
            row.RelativeItem().Element(c => KpiCell(c, "Tested objects", kpi.TestedObjectCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Opportunities", kpi.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Defect bits", kpi.DefectBitCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "DPMO (ppm)", kpi.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture)));
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

    private static void ComposeRows(IContainer container, DpmoTableResult result)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(80);
                c.RelativeColumn(3);
                c.ConstantColumn(80);
                c.ConstantColumn(80);
                c.ConstantColumn(80);
                c.ConstantColumn(80);
            });
            PanelYieldPdfRenderer.HeaderCell(t, "Group key");
            PanelYieldPdfRenderer.HeaderCell(t, "Group name");
            PanelYieldPdfRenderer.HeaderCell(t, "Tested objs");
            PanelYieldPdfRenderer.HeaderCell(t, "Opportunities");
            PanelYieldPdfRenderer.HeaderCell(t, "Defect bits");
            PanelYieldPdfRenderer.HeaderCell(t, "DPMO");

            foreach (var row in result.Rows)
            {
                PanelYieldPdfRenderer.Cell(t, row.GroupKey ?? string.Empty);
                PanelYieldPdfRenderer.Cell(t, row.GroupName ?? string.Empty);
                PanelYieldPdfRenderer.Cell(t, row.Kpi.TestedObjectCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.DefectBitCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.Kpi.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture));
            }
        });
    }
}
