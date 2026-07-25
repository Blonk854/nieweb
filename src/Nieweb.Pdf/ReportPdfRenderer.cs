using System.Globalization;
using Nieweb.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a multi-tile <em>saved report</em> to PDF (docs/phase-2.md
/// §7.6 <c>RC5</c>). The report contract is a small cover (title,
/// description, source, window, tile list) followed by one section
/// per tile, in the report's <c>DisplayOrder</c>.
/// </summary>
/// <remarks>
/// Uses the shared <see cref="NiewebPdfDocument"/> so the header
/// (Nieweb wordmark), BSS Green Premium strip, and footer
/// (<c>Nieweb · Page N of M</c>) match the per-tile PDF exports.
/// Unsupported tile types render a placeholder section rather than
/// failing the whole download; users see which tile did not render
/// and can raise a bug against Nieweb.Pdf without losing the other
/// tiles.
/// </remarks>
public static class ReportPdfRenderer
{
    /// <summary>Header + section body for a single tile.</summary>
    public sealed record TileSection(
        string Title,
        string TileType,
        object? Result);

    /// <summary>Metadata shared across all sections.</summary>
    public sealed record ReportHeader(
        string ReportTitle,
        string? Description,
        string SourceId,
        string SourceDisplayName,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndExclusiveUtc);

    /// <summary>
    /// Result payload for a free-text comment tile (docs/phase-2.md
    /// §7.6 <c>RC6</c>). <see cref="Markdown"/> is rendered as plain
    /// text in the PDF/XLSX exports — the renderer intentionally does
    /// not implement a full markdown parser; that stays a SPA-side
    /// concern. Empty or whitespace-only markdown still counts as
    /// "rendered" so the cover status column stays consistent.
    /// </summary>
    public sealed record CommentTileResult(string? Markdown);

    /// <summary>
    /// Write a full multi-tile report PDF into <paramref name="destination"/>.
    /// The <paramref name="sections"/> list is rendered in-order; an
    /// empty list still produces the cover so users get a valid PDF
    /// even for an empty report.
    /// </summary>
    /// <param name="header">Metadata shared across all sections (title, source, window).</param>
    /// <param name="sections">Tile results in <c>DisplayOrder</c>. May be empty.</param>
    /// <param name="generatedByDisplayName">User-facing name printed in the footer.</param>
    /// <param name="destination">Target stream (typically an HTTP response body).</param>
    /// <param name="generatedAt">Rendering timestamp; defaults to <c>DateTimeOffset.UtcNow</c>.</param>
    /// <param name="timeZone">
    /// Optional display time zone. Every timestamp in the header,
    /// meta table, and body honours this zone; when null the renderer
    /// falls back to UTC. Aggregation (which panels/subpanels are
    /// included in the window) is always driven by the UTC bounds in
    /// <paramref name="header"/> and never affected by this argument.
    /// </param>
    public static void Render(
        ReportHeader header,
        IReadOnlyList<TileSection> sections,
        string generatedByDisplayName,
        Stream destination,
        DateTimeOffset? generatedAt = null,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(generatedByDisplayName);
        ArgumentNullException.ThrowIfNull(destination);

        var tz = timeZone ?? TimeZoneInfo.Utc;
        var subtitle = NiewebPdfTimestamps.FormatSubtitle(
            $"Source: {header.SourceDisplayName}",
            $"Window: {NiewebPdfTimestamps.FormatRange(header.WindowStartUtc, header.WindowEndExclusiveUtc, tz)}");

        var doc = new NiewebPdfDocument(
            title: header.ReportTitle,
            subtitle: subtitle,
            generatedByDisplayName: generatedByDisplayName,
            generatedAt: generatedAt ?? DateTimeOffset.UtcNow,
            body: body => Compose(body, header, sections, tz),
            timeZone: tz);

        doc.Render(destination);
    }

    private static void Compose(
        IContainer body,
        ReportHeader header,
        IReadOnlyList<TileSection> sections,
        TimeZoneInfo timeZone)
    {
        body.Column(col =>
        {
            col.Spacing(12);
            col.Item().Element(c => ComposeCover(c, header, sections, timeZone));

            for (var i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                var index = i + 1;
                col.Item().PageBreak();
                col.Item().Element(c => ComposeSection(c, index, section));
            }
        });
    }

    private static void ComposeCover(
        IContainer container,
        ReportHeader header,
        IReadOnlyList<TileSection> sections,
        TimeZoneInfo timeZone)
    {
        container.Column(col =>
        {
            col.Spacing(8);
            col.Item().Text("Report contents").SemiBold().FontSize(12);
            if (!string.IsNullOrWhiteSpace(header.Description))
            {
                col.Item().Text(header.Description!).FontSize(9).FontColor(Colors.Grey.Darken2);
            }
            col.Item().Element(c => MetaTable(c, header, timeZone));
            col.Item().Text(string.Create(CultureInfo.InvariantCulture,
                    $"{sections.Count} tile(s):"))
                .SemiBold().FontSize(10);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(28);   // #
                    cd.RelativeColumn(4);    // Title
                    cd.RelativeColumn(3);    // Tile type
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("#");
                    h.Cell().Element(HeaderCell).Text("Tile title");
                    h.Cell().Element(HeaderCell).Text("Type");
                });
                for (var i = 0; i < sections.Count; i++)
                {
                    var s = sections[i];
                    table.Cell().Element(BodyCell).Text((i + 1).ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).Text(s.Title);
                    table.Cell().Element(BodyCell).Text(s.TileType);
                }
            });
        });
    }

    private static void MetaTable(IContainer container, ReportHeader header, TimeZoneInfo timeZone)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Table(table =>
        {
            table.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(1);
                cd.RelativeColumn(3);
            });
            void Row(string label, string value)
            {
                table.Cell().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                table.Cell().Text(value).FontSize(9);
            }
            Row("Source Id", header.SourceId);
            Row("Source",    header.SourceDisplayName);
            Row("Window",    NiewebPdfTimestamps.FormatRange(
                                header.WindowStartUtc,
                                header.WindowEndExclusiveUtc,
                                timeZone) + " (end exclusive)");
        });
    }

    private static void ComposeSection(IContainer container, int index, TileSection section)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text(string.Create(CultureInfo.InvariantCulture,
                    $"Tile {index}: {section.Title}"))
                .SemiBold().FontSize(12);
            col.Item().Text(string.Create(CultureInfo.InvariantCulture,
                    $"Type: {section.TileType}"))
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            switch (section.Result)
            {
                case PanelYieldResult panelYield:
                    col.Item().Element(c => ComposePanelYield(c, panelYield));
                    break;
                case ParetoResult pareto:
                    col.Item().Element(c => ComposePareto(c, pareto));
                    break;
                case CommentTileResult comment:
                    col.Item().Element(c => ComposeComment(c, comment));
                    break;
                default:
                    col.Item().Text(string.Create(CultureInfo.InvariantCulture,
                            $"Unsupported tile type '{section.TileType}' - please open a Nieweb.Pdf issue."))
                        .FontSize(9).Italic().FontColor(Colors.Red.Darken1);
                    break;
            }
        });
    }

    // -------------------- panelYield mini-composer --------------------

    private static void ComposePanelYield(IContainer container, PanelYieldResult result)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Row(row =>
            {
                var kpi = result.Overall;
                row.RelativeItem().Element(c => KpiCell(c, "Total",         Long(kpi.TotalPanels)));
                row.RelativeItem().Element(c => KpiCell(c, "Inspected",     Long(kpi.InspectedPanels)));
                row.RelativeItem().Element(c => KpiCell(c, "Good",          Long(kpi.GoodPanels)));
                row.RelativeItem().Element(c => KpiCell(c, "Faulty",        Long(kpi.FaultyPanels)));
                row.RelativeItem().Element(c => KpiCell(c, "Not inspected", Long(kpi.NotInspectedPanels)));
                row.RelativeItem().Element(c => KpiCell(c, "FPY (%)",       kpi.FpyPercent.ToString("0.00", CultureInfo.InvariantCulture)));
            });
            col.Item().Text("By AOI machine").SemiBold().FontSize(10);
            col.Item().Table(table =>
            {
                // Columns match the XLSX WritePanelYieldSheet layout so
                // the two exports contain the same data. Every real column
                // has a fixed width and a trailing relative spacer absorbs
                // the leftover page width - this packs the columns together
                // on the left (instead of letting the Machine column blow
                // open a gap in the middle) and guarantees the final FPY
                // column can never spill past the right margin.
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(32);   // Id
                    cd.ConstantColumn(110);  // Machine
                    cd.ConstantColumn(60);   // Total
                    cd.ConstantColumn(72);   // Inspected (widest header word)
                    cd.ConstantColumn(58);   // Good
                    cd.ConstantColumn(56);   // Faulty
                    cd.ConstantColumn(62);   // Not inspected
                    cd.ConstantColumn(56);   // FPY %
                    cd.RelativeColumn(1);    // Spacer (absorbs remaining width)
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("Id");
                    h.Cell().Element(HeaderCell).Text("Machine");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Total");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Inspected");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Good");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Faulty");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Not insp.");
                    h.Cell().Element(HeaderCell).AlignRight().Text("FPY %");
                    h.Cell().Element(HeaderCell).Text(string.Empty);
                });
                foreach (var m in result.ByMachine)
                {
                    table.Cell().Element(BodyCell).Text(Int(m.MachineId));
                    table.Cell().Element(BodyCell).Text(m.MachineName ?? string.Empty);
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(m.Kpi.TotalPanels));
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(m.Kpi.InspectedPanels));
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(m.Kpi.GoodPanels));
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(m.Kpi.FaultyPanels));
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(m.Kpi.NotInspectedPanels));
                    table.Cell().Element(BodyCell).AlignRight().Text(m.Kpi.FpyPercent.ToString("0.00", CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).Text(string.Empty);
                }
            });
        });
    }

    // -------------------- pareto mini-composer --------------------

    private static void ComposePareto(IContainer container, ParetoResult result)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text(string.Create(CultureInfo.InvariantCulture,
                    $"Axis: {result.Axis}  ·  Numerator: {result.Numerator}  ·  Weight: {result.Weight}"))
                .FontSize(9).FontColor(Colors.Grey.Darken2);
            col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Row(row =>
            {
                var kpi = result.Overall;
                row.RelativeItem().Element(c => KpiCell(c, "Defect bits",   Long(kpi.DefectBitCount)));
                row.RelativeItem().Element(c => KpiCell(c, "Opportunities", Long(kpi.OpportunityCount)));
                row.RelativeItem().Element(c => KpiCell(c, "DPMO (ppm)",    kpi.DpmoPpm.ToString("0.##", CultureInfo.InvariantCulture)));
            });
            col.Item().Text("Rows").SemiBold().FontSize(10);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(4);   // Group
                    cd.RelativeColumn(1);   // Count
                    cd.RelativeColumn(1);   // Share %
                    cd.RelativeColumn(1);   // Cumul %
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("Group");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Count");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Share %");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Cumul %");
                });
                foreach (var r in result.Rows)
                {
                    table.Cell().Element(BodyCell).Text(r.GroupName ?? r.GroupKey ?? "-");
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(r.DefectCount));
                    table.Cell().Element(BodyCell).AlignRight().Text(r.DefectSharePercent.ToString("0.00", CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).AlignRight().Text(r.CumulativePercent.ToString("0.00", CultureInfo.InvariantCulture));
                }
                if (result.OthersBucket is { } others)
                {
                    table.Cell().Element(BodyCell).Text(others.GroupName ?? "Others");
                    table.Cell().Element(BodyCell).AlignRight().Text(Long(others.DefectCount));
                    table.Cell().Element(BodyCell).AlignRight().Text(others.DefectSharePercent.ToString("0.00", CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).AlignRight().Text(others.CumulativePercent.ToString("0.00", CultureInfo.InvariantCulture));
                }
            });
        });
    }

    // -------------------- comment mini-composer --------------------

    private static void ComposeComment(IContainer container, CommentTileResult comment)
    {
        var text = comment.Markdown;
        if (string.IsNullOrWhiteSpace(text))
        {
            container.Text("(empty comment)")
                .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
            return;
        }
        // The PDF renderer treats the markdown as plain text — each
        // source paragraph becomes its own <c>Text</c> block so long
        // comments wrap naturally at page breaks. A proper markdown
        // parser (headings / bold / lists) is a Phase 3 concern.
        container.Column(col =>
        {
            col.Spacing(4);
            var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                 .Split("\n\n", StringSplitOptions.None);
            foreach (var paragraph in paragraphs)
            {
                var body = paragraph.Trim();
                if (body.Length == 0)
                {
                    continue;
                }
                col.Item().Text(body).FontSize(10);
            }
        });
    }

    // -------------------- helpers --------------------

    private static void KpiCell(IContainer c, string label, string value)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(value).SemiBold().FontSize(12);
        });
    }

    private static IContainer HeaderCell(IContainer c)
        => c.DefaultTextStyle(t => t.SemiBold())
            .PaddingVertical(2)
            .PaddingHorizontal(3)
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten1);

    private static IContainer BodyCell(IContainer c)
        => c.PaddingVertical(2)
            .PaddingHorizontal(3)
            .BorderBottom(0.25f)
            .BorderColor(Colors.Grey.Lighten2);

    private static string Int(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string Long(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
}
