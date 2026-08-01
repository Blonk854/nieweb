using System.Globalization;

using Nieweb.Reports;
using Nieweb.Reports.Common;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders the per-line FPY trend to PDF using the fixed Nieweb corporate
/// template: one section per source, each with a multi-series trend chart
/// (<see cref="FpyTrendChartSvg"/>) and a per-line window-total table for the
/// three FPY flavours.
/// </summary>
public static class FpyTrendPdfRenderer
{
    /// <summary>Write the PDF into <paramref name="destination"/>.</summary>
    public static void Render(
        IReadOnlyList<FpyTrendResult> sources,
        TimeBucket bucket,
        FpyGranularity granularity,
        SkipExclusion skipExclusion,
        FpyFlavor flavor,
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
            $"Bucket: {bucket}   Granularity: {granularity}   Skip: {skipExclusion}   Flavour: {flavor}");

        string window = sources.Count > 0
            ? NiewebPdfTimestamps.FormatRange(
                sources[0].Window.StartUtc, sources[0].Window.EndUtcExclusive, tz)
            : "—";

        var doc = new NiewebPdfDocument(
            title: "FPY Trend",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, sources, flavor),
            timeZone: tz,
            footerNote: $"Sources: {sources.Count}   ·   Window: {window}");

        doc.Render(destination);
    }

    private static void Compose(IContainer body, IReadOnlyList<FpyTrendResult> sources, FpyFlavor flavor)
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
                col.Item().Element(c => ComposeSource(c, source, flavor));
            }
        });
    }

    private static void ComposeSource(IContainer container, FpyTrendResult source, FpyFlavor flavor)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text($"{source.Source.DisplayName}  ·  {source.Lines.Count} line(s)")
               .SemiBold().FontSize(12);

            if (source.Lines.Count > 0 && source.Buckets.Count > 0)
            {
                col.Item().Height(200).Svg(FpyTrendChartSvg.Build(source.Buckets, source.Lines, flavor));
            }
            else
            {
                col.Item().Text("No lines in this window.").FontSize(9).FontColor(Colors.Grey.Darken1);
            }

            col.Item().Element(c => ComposeLineTable(c, source));
        });
    }

    private static void ComposeLineTable(IContainer container, FpyTrendResult source)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.ConstantColumn(70);
                c.ConstantColumn(75);
                c.ConstantColumn(85);
                c.ConstantColumn(85);
            });
            PanelYieldPdfRenderer.HeaderCell(t, "Line");
            PanelYieldPdfRenderer.HeaderCell(t, "Inspected");
            PanelYieldPdfRenderer.HeaderCell(t, "FPY AOI (%)");
            PanelYieldPdfRenderer.HeaderCell(t, "FPY Diag (%)");
            PanelYieldPdfRenderer.HeaderCell(t, "FPY A/Rep (%)");

            foreach (var line in source.Lines)
            {
                PanelYieldPdfRenderer.Cell(t, line.MachineName ?? string.Create(CultureInfo.InvariantCulture, $"#{line.MachineId}"));
                PanelYieldPdfRenderer.Cell(t, line.Overall.InspectedCount.ToString("N0", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, line.Overall.FpyAoiPercent.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, line.Overall.FpyDiagnosticPercent.ToString("0.00", CultureInfo.InvariantCulture));
                PanelYieldPdfRenderer.Cell(t, line.Overall.FpyAfterRepairPercent.ToString("0.00", CultureInfo.InvariantCulture));
            }
        });
    }
}
