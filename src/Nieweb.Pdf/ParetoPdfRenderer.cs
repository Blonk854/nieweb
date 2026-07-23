using System.Globalization;
using Nieweb.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a <see cref="ParetoResult"/> to PDF. Because a PDF cannot
/// hold an interactive chart, we emit the same tabular data ECharts
/// consumes on the SPA (rank / group / defect count / opportunity /
/// DPMO / defect % / cumulative %) — the visual bar-chart lives in
/// the browser view and its PNG can be attached to the PDF later via
/// a chart-image endpoint.
/// </summary>
public static class ParetoPdfRenderer
{
    /// <summary>Write the PDF into <paramref name="destination"/>.</summary>
    /// <param name="result">Report result to render.</param>
    /// <param name="generatedByDisplayName">User-facing name printed in the footer.</param>
    /// <param name="destination">Target stream.</param>
    /// <param name="generatedAt">Rendering timestamp (defaults to now UTC).</param>
    public static void Render(
        ParetoResult result,
        string generatedByDisplayName,
        Stream destination,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(generatedByDisplayName);
        ArgumentNullException.ThrowIfNull(destination);

        var subtitle = PanelYieldPdfRenderer.FormatSubtitle(
            $"Source: {result.Source.DisplayName}",
            $"Axis: {result.Axis}   Numerator: {result.Numerator}   Opportunity: {result.Opportunity}   Weight: {result.Weight}",
            $"Window: {PanelYieldPdfRenderer.FormatUtc(result.Window.StartUtc)} → {PanelYieldPdfRenderer.FormatUtc(result.Window.EndUtcExclusive)}");

        var doc = new NiewebPdfDocument(
            title: "Pareto — Defects",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, result));

        doc.Render(destination);
    }

    private static void Compose(IContainer body, ParetoResult result)
    {
        body.Column(col =>
        {
            col.Spacing(10);
            col.Item().Element(c => ComposeOverall(c, result));
            col.Item().Text($"Ranked rows ({result.Rows.Count}{(result.OthersBucket is null ? "" : " + Others")})")
               .SemiBold().FontSize(11);
            col.Item().Element(c => ComposeRows(c, result));
        });
    }

    private static void ComposeOverall(IContainer container, ParetoResult result)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Row(row =>
        {
            row.RelativeItem().Element(c => KpiCell(c, "Total defects", result.Overall.DefectBitCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Opportunities", result.Overall.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Overall DPMO", result.Overall.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture)));
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

    private static void ComposeRows(IContainer container, ParetoResult result)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(30);
                c.RelativeColumn(3);
                c.ConstantColumn(70);
                c.ConstantColumn(80);
                c.ConstantColumn(70);
                c.ConstantColumn(70);
                c.ConstantColumn(75);
                c.ConstantColumn(30);
            });
            PanelYieldPdfRenderer.HeaderCell(t, "#");
            PanelYieldPdfRenderer.HeaderCell(t, "Group");
            PanelYieldPdfRenderer.HeaderCell(t, "Defects");
            PanelYieldPdfRenderer.HeaderCell(t, "Opportunities");
            PanelYieldPdfRenderer.HeaderCell(t, "DPMO");
            PanelYieldPdfRenderer.HeaderCell(t, "Defect %");
            PanelYieldPdfRenderer.HeaderCell(t, "Cumulative %");
            PanelYieldPdfRenderer.HeaderCell(t, "★");

            var rank = 1;
            foreach (var row in result.Rows)
            {
                PanelYieldPdfRenderer.Cell(t, rank++.ToString(CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.GroupName ?? row.GroupKey ?? string.Empty);
                PanelYieldPdfRenderer.Cell(t, row.DefectCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.DefectSharePercent.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.CumulativePercent.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, row.IsVitalFew ? "★" : string.Empty);
            }

            if (result.OthersBucket is { } others)
            {
                PanelYieldPdfRenderer.Cell(t, "—");
                PanelYieldPdfRenderer.Cell(t, "Others");
                PanelYieldPdfRenderer.Cell(t, others.DefectCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, others.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, others.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, others.DefectSharePercent.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, others.CumulativePercent.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, string.Empty);
            }
        });
    }
}
