using System.Globalization;
using Nieweb.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a <see cref="PanelYieldResult"/> to PDF using the fixed
/// Nieweb corporate template. Layout matches the Panel Yield HTML
/// view: overall KPI card at the top, then a per-machine breakdown
/// table sorted the same way the report emits it.
/// </summary>
public static class PanelYieldPdfRenderer
{
    /// <summary>
    /// Write the PDF into <paramref name="destination"/>. Same content
    /// as the CSV / XLSX exports so downstream consumers see numeric
    /// parity across formats.
    /// </summary>
    /// <param name="result">Report result to render.</param>
    /// <param name="generatedByDisplayName">User-facing name printed in the footer.</param>
    /// <param name="destination">Target stream (typically an HTTP response body).</param>
    /// <param name="generatedAt">Rendering timestamp (defaults to now UTC).</param>
    public static void Render(
        PanelYieldResult result,
        string generatedByDisplayName,
        Stream destination,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(generatedByDisplayName);
        ArgumentNullException.ThrowIfNull(destination);

        var subtitle = FormatSubtitle(
            $"Source: {result.Source.DisplayName}",
            $"Window: {FormatUtc(result.Window.StartUtc)} → {FormatUtc(result.Window.EndUtcExclusive)}");

        var doc = new NiewebPdfDocument(
            title: "Panel Yield by Line",
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, result));

        doc.Render(destination);
    }

    private static void Compose(IContainer body, PanelYieldResult result)
    {
        body.Column(col =>
        {
            col.Spacing(10);
            col.Item().Element(c => ComposeOverall(c, result.Overall));
            col.Item().Text("By AOI machine").SemiBold().FontSize(11);
            col.Item().Element(c => ComposeByMachine(c, result));
        });
    }

    private static void ComposeOverall(IContainer container, PanelYieldKpi kpi)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Row(row =>
        {
            row.RelativeItem().Element(c => KpiCell(c, "Total panels", kpi.TotalPanels.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Inspected", kpi.InspectedPanels.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Good", kpi.GoodPanels.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Faulty", kpi.FaultyPanels.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "Not inspected", kpi.NotInspectedPanels.ToString("N0", CultureInfo.InvariantCulture)));
            row.RelativeItem().Element(c => KpiCell(c, "FPY (%)", kpi.FpyPercent.ToString("0.00", CultureInfo.InvariantCulture)));
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

    private static void ComposeByMachine(IContainer container, PanelYieldResult result)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(60);
                c.RelativeColumn(3);
                c.ConstantColumn(70);
                c.ConstantColumn(70);
                c.ConstantColumn(70);
                c.ConstantColumn(70);
                c.ConstantColumn(80);
                c.ConstantColumn(70);
            });
            HeaderCell(t, "Machine Id");
            HeaderCell(t, "Machine name");
            HeaderCell(t, "Total");
            HeaderCell(t, "Inspected");
            HeaderCell(t, "Good");
            HeaderCell(t, "Faulty");
            HeaderCell(t, "Not inspected");
            HeaderCell(t, "FPY (%)");

            foreach (var m in result.ByMachine)
            {
                Cell(t, m.MachineId.ToString(CultureInfo.InvariantCulture));
                Cell(t, m.MachineName ?? string.Empty);
                Cell(t, m.Kpi.TotalPanels.ToString("N0", CultureInfo.InvariantCulture));
                Cell(t, m.Kpi.InspectedPanels.ToString("N0", CultureInfo.InvariantCulture));
                Cell(t, m.Kpi.GoodPanels.ToString("N0", CultureInfo.InvariantCulture));
                Cell(t, m.Kpi.FaultyPanels.ToString("N0", CultureInfo.InvariantCulture));
                Cell(t, m.Kpi.NotInspectedPanels.ToString("N0", CultureInfo.InvariantCulture));
                Cell(t, m.Kpi.FpyPercent.ToString("0.00", CultureInfo.InvariantCulture));
            }
        });
    }

    internal static void HeaderCell(TableDescriptor t, string text)
    {
        t.Cell().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Padding(3).Text(text).SemiBold().FontSize(9);
    }

    internal static void Cell(TableDescriptor t, string text)
    {
        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2)
         .Padding(3).Text(text).FontSize(9);
    }

    internal static string FormatUtc(DateTimeOffset dto) =>
        dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    internal static string FormatSubtitle(params string[] parts)
        => string.Join("   ·   ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
