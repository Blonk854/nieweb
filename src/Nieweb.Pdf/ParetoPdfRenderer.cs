using System.Globalization;
using Nieweb.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a <see cref="ParetoResult"/> to PDF: the same volume-weighted
/// bar + cumulative-percent chart the SPA draws (as a native SVG via
/// <see cref="ParetoChartSvg"/>), followed by the ranked data table
/// (rank / group / defect count / opportunity / DPMO / defect % /
/// cumulative %).
/// </summary>
public static class ParetoPdfRenderer
{
    /// <summary>Write the PDF into <paramref name="destination"/>.</summary>
    /// <param name="result">Report result to render.</param>
    /// <param name="generatedByDisplayName">User-facing name printed in the footer.</param>
    /// <param name="destination">Target stream.</param>
    /// <param name="generatedAt">Rendering timestamp (defaults to now UTC).</param>
    /// <param name="timeZone">Display time zone (defaults to UTC when null).</param>
    /// <param name="vitalFewThresholdPercent">
    /// Vital-few cumulative-% threshold to draw as the dashed line on the
    /// chart (defaults to 80).
    /// </param>
    public static void Render(
        ParetoResult result,
        string generatedByDisplayName,
        Stream destination,
        DateTimeOffset? generatedAt = null,
        TimeZoneInfo? timeZone = null,
        double vitalFewThresholdPercent = 80)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(generatedByDisplayName);
        ArgumentNullException.ThrowIfNull(destination);

        var tz = timeZone ?? TimeZoneInfo.Utc;
        var subtitle = NiewebPdfTimestamps.FormatSubtitle(
            $"Axis: {result.Axis}   Numerator: {result.Numerator}   Opportunity: {result.Opportunity}   Weight: {result.Weight}");
        var window = NiewebPdfTimestamps.FormatRange(
            result.Window.StartUtc, result.Window.EndUtcExclusive, tz);

        var doc = new NiewebPdfDocument(
            title: "Pareto — Defects",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, result, vitalFewThresholdPercent),
            timeZone: tz,
            footerNote: $"Source: {result.Source.DisplayName}   ·   Window: {window}");

        doc.Render(destination);
    }

    private static void Compose(IContainer body, ParetoResult result, double vitalFewThresholdPercent)
    {
        body.Column(col =>
        {
            col.Spacing(10);
            col.Item().Element(c => ComposeOverall(c, result));
            if (result.Rows.Count > 0)
            {
                col.Item().Height(230).Svg(ParetoChartSvg.Build(result, vitalFewThresholdPercent));
            }
            col.Item().Text($"Ranked rows ({result.Rows.Count}{(result.OthersBucket is null ? "" : " + Others")})")
               .SemiBold().FontSize(11);
            col.Item().Element(c => ComposeRows(c, result));
            if (!ParetoReport.OpportunitiesApplicableForAxis(result.Axis))
            {
                col.Item().Text(
                    "N/A: opportunity counts and DPMO are not measured per reference designator, part number, or JEDEC. Overall DPMO still uses card test counts.")
                    .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
            }
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
        var showCumulative = ParetoPresentation.ShowCumulative(result.Weight);
        const string na = "—";
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
                if (showCumulative)
                {
                    c.ConstantColumn(75);
                    c.ConstantColumn(30);
                }
            });
            PanelYieldPdfRenderer.HeaderCell(t, "#");
            PanelYieldPdfRenderer.HeaderCell(t, "Group");
            PanelYieldPdfRenderer.HeaderCell(t, "Defects");
            PanelYieldPdfRenderer.HeaderCell(t, "Opportunities");
            PanelYieldPdfRenderer.HeaderCell(t, "DPMO");
            PanelYieldPdfRenderer.HeaderCell(t, "Defect %");
            if (showCumulative)
            {
                PanelYieldPdfRenderer.HeaderCell(t, "Cumulative %");
                PanelYieldPdfRenderer.HeaderCell(t, "★");
            }

            var rank = 1;
            foreach (var row in result.Rows)
            {
                WritePdfDataRow(t, rank++.ToString(CultureInfo.InvariantCulture), row, showCumulative, na);
            }

            if (result.OthersBucket is { } others)
            {
                WritePdfDataRow(t, "—", others, showCumulative, na);
            }
        });
    }

    private static void WritePdfDataRow(
        TableDescriptor t,
        string rank,
        ParetoRow row,
        bool showCumulative,
        string na)
    {
        PanelYieldPdfRenderer.Cell(t, rank);
        PanelYieldPdfRenderer.Cell(t, row.GroupName ?? row.GroupKey ?? string.Empty);
        PanelYieldPdfRenderer.Cell(t, row.DefectCount.ToString("N0", CultureInfo.InvariantCulture));
        PanelYieldPdfRenderer.Cell(t, row.OpportunitiesApplicable
            ? row.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture)
            : na);
        PanelYieldPdfRenderer.Cell(t, ParetoPresentation.DpmoCell(row, na));
        PanelYieldPdfRenderer.Cell(t, row.DefectSharePercent.ToString("0.00", CultureInfo.InvariantCulture));
        if (showCumulative)
        {
            PanelYieldPdfRenderer.Cell(t, row.CumulativePercent.ToString("0.00", CultureInfo.InvariantCulture));
            PanelYieldPdfRenderer.Cell(t, row.IsVitalFew ? "★" : string.Empty);
        }
    }
}
