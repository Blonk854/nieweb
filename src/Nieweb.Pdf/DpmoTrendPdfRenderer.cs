using System.Globalization;

using Nieweb.Reports;
using Nieweb.Reports.Common;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders the per-line DPMO trend to PDF using the fixed Nieweb corporate
/// template: one section per source, each with a multi-series trend chart
/// (<see cref="DpmoTrendChartSvg"/>) and a per-line window-total table
/// carrying the opportunity denominator and all three DPMO flavours.
/// </summary>
public static class DpmoTrendPdfRenderer
{
    /// <summary>Write the PDF into <paramref name="destination"/>.</summary>
    public static void Render(
        IReadOnlyList<DpmoTrendResult> sources,
        TimeBucket bucket,
        DpmoOpportunity opportunity,
        SkipExclusion skipExclusion,
        DpmoNumerator numerator,
        string generatedByDisplayName,
        Stream destination,
        DateTimeOffset? generatedAt = null,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(generatedByDisplayName);
        ArgumentNullException.ThrowIfNull(destination);

        var tz = timeZone ?? TimeZoneInfo.Utc;
        var subtitle = NiewebPdfTimestamps.FormatSubtitle(
            $"Bucket: {bucket}   Opportunity: {opportunity}   Skip: {skipExclusion}   Numerator: {numerator}");

        string window = sources.Count > 0
            ? NiewebPdfTimestamps.FormatRange(
                sources[0].Window.StartUtc, sources[0].Window.EndUtcExclusive, tz)
            : "—";

        var doc = new NiewebPdfDocument(
            title: "DPMO Trend",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, sources, numerator),
            timeZone: tz,
            footerNote: $"Sources: {sources.Count}   ·   Window: {window}");

        doc.Render(destination);
    }

    private static void Compose(IContainer body, IReadOnlyList<DpmoTrendResult> sources, DpmoNumerator numerator)
    {
        body.Column(col =>
        {
            col.Spacing(12);
            if (sources.Count == 0)
            {
                col.Item().Text("No sources returned data for this window.").Italic();
                return;
            }
            foreach (var source in sources)
            {
                col.Item().Element(c => ComposeSource(c, source, numerator));
            }
        });
    }

    private static void ComposeSource(IContainer container, DpmoTrendResult source, DpmoNumerator numerator)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text($"{source.Source.DisplayName}  ·  {source.Lines.Count} line(s)")
               .SemiBold().FontSize(12);

            if (source.Lines.Count > 0 && source.Buckets.Count > 0)
            {
                col.Item().Height(200).Svg(DpmoTrendChartSvg.Build(source.Buckets, source.Lines, numerator));
            }
            else
            {
                col.Item().Text("No lines in this window.").FontSize(9).FontColor(Colors.Grey.Darken1);
            }

            col.Item().Element(c => ComposeLineTable(c, source));
        });
    }

    private static void ComposeLineTable(IContainer container, DpmoTrendResult source)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.ConstantColumn(90);
                c.ConstantColumn(75);
                c.ConstantColumn(75);
                c.ConstantColumn(75);
            });
            PanelYieldPdfRenderer.HeaderCell(t, "Line");
            PanelYieldPdfRenderer.HeaderCell(t, "Opportunities");
            PanelYieldPdfRenderer.HeaderCell(t, "DPMO AOI");
            PanelYieldPdfRenderer.HeaderCell(t, "DPMO Real");
            PanelYieldPdfRenderer.HeaderCell(t, "DPMO Dummy");

            foreach (var line in source.Lines)
            {
                PanelYieldPdfRenderer.Cell(t, line.MachineName ?? string.Create(CultureInfo.InvariantCulture, $"#{line.MachineId}"));
                PanelYieldPdfRenderer.Cell(t, line.Overall.OpportunityCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, line.Overall.DpmoAoi.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, line.Overall.DpmoReal.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, line.Overall.DpmoDummy.ToString("0.00", CultureInfo.InvariantCulture));
            }
        });
    }
}
