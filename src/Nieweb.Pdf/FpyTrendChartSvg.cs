using System.Globalization;
using System.Text;

using Nieweb.Reports;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a per-source FPY trend as a self-contained multi-series SVG line
/// chart (one polyline per AOI line, FPY% of the selected flavour over the
/// day / week buckets, with faint green/amber/red band guides). Embedded in
/// the FPY-trend PDF via QuestPDF's <c>IContainer.Svg(string)</c>.
/// </summary>
internal static class FpyTrendChartSvg
{
    private const string AxisColor = "#adb5bd";
    private const string GridColor = "#e9ecef";
    private const string TextColor = "#495057";
    private const string GreenColor = "#2f9e44";
    private const string AmberColor = "#f08c00";

    private const double GreenThreshold = 99.5;
    private const double AmberThreshold = 98.0;

    // Distinct-enough line palette (Mantine-ish hues).
    private static readonly string[] Palette =
    [
        "#1c7ed6", "#f08c00", "#2f9e44", "#e8590c", "#7048e8",
        "#0c8599", "#c2255c", "#5c940d", "#495057", "#d6336c",
    ];

    private const double W = 960;
    private const double H = 320;
    private const double PlotLeft = 54;
    private const double PlotRight = 906;
    private const double PlotTop = 30;
    private const double PlotBottom = 214;

    public static string Build(
        IReadOnlyList<FpyTrendBucket> buckets,
        IReadOnlyList<FpyTrendLine> lines,
        FpyFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(lines);

        var sb = new StringBuilder(4096);
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(W)} {F(H)}\" font-family=\"Helvetica, Arial, sans-serif\">");

        if (buckets.Count == 0 || lines.Count == 0)
        {
            sb.Append("</svg>");
            return sb.ToString();
        }

        const double plotWidth = PlotRight - PlotLeft;
        const double plotHeight = PlotBottom - PlotTop;
        int n = buckets.Count;

        // X for a bucket index. A single bucket sits centred.
        double X(int i) => n == 1 ? (PlotLeft + PlotRight) / 2.0 : PlotLeft + (plotWidth * i / (n - 1));

        // Y range: zoom in when every plotted value is high, else full 0..100.
        double minValue = double.PositiveInfinity;
        foreach (var line in lines)
        {
            foreach (var p in line.Points)
            {
                var v = Select(p.Kpi, flavor);
                if (v < minValue)
                {
                    minValue = v;
                }
            }
        }
        if (!double.IsFinite(minValue))
        {
            minValue = 0;
        }
        double yMin = minValue >= 90 ? Math.Max(0, Math.Floor(minValue) - 2) : 0;
        const double yMax = 100;
        double range = yMax - yMin <= 0 ? 1 : yMax - yMin;
        double Y(double v) => PlotBottom - ((v - yMin) / range) * plotHeight;

        // Gridlines + left % labels.
        for (int g = 0; g <= 4; g++)
        {
            double frac = g / 4.0;
            double val = yMin + (frac * range);
            double y = PlotBottom - (frac * plotHeight);
            sb.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{F(PlotLeft)}\" y1=\"{F(y)}\" x2=\"{F(PlotRight)}\" y2=\"{F(y)}\" stroke=\"{GridColor}\" stroke-width=\"0.75\"/>");
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{F(PlotLeft - 5)}\" y=\"{F(y + 3)}\" text-anchor=\"end\" font-size=\"9\" fill=\"{TextColor}\">{Pct(val)}%</text>");
        }

        // Axis frame.
        sb.Append(Line(PlotLeft, PlotTop, PlotLeft, PlotBottom, AxisColor, 1));
        sb.Append(Line(PlotLeft, PlotBottom, PlotRight, PlotBottom, AxisColor, 1));

        // Faint threshold guides.
        Guide(sb, Y, GreenThreshold, yMin, yMax, GreenColor);
        Guide(sb, Y, AmberThreshold, yMin, yMax, AmberColor);

        // X-axis bucket labels (thin when crowded).
        int labelEvery = n > 16 ? (int)Math.Ceiling(n / 16.0) : 1;
        for (int i = 0; i < n; i++)
        {
            if (i % labelEvery != 0 && i != n - 1)
            {
                continue;
            }
            double cx = X(i);
            string label = Escape(Truncate(buckets[i].Label, 12));
            double ly = PlotBottom + 12;
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{F(cx)}\" y=\"{F(ly)}\" text-anchor=\"end\" font-size=\"8\" fill=\"{TextColor}\" transform=\"rotate(-35 {F(cx)} {F(ly)})\">{label}</text>");
        }

        // One polyline per line (broken at gaps), plus dots.
        for (int li = 0; li < lines.Count; li++)
        {
            var color = Palette[li % Palette.Length];
            var line = lines[li];
            var present = line.Points.ToDictionary(p => p.BucketIndex, p => Select(p.Kpi, flavor));

            var segment = new List<string>();
            void FlushSegment()
            {
                if (segment.Count >= 2)
                {
                    sb.Append(CultureInfo.InvariantCulture,
                        $"<polyline points=\"{string.Join(' ', segment)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>");
                }
                segment.Clear();
            }
            for (int i = 0; i < n; i++)
            {
                if (present.TryGetValue(i, out var v))
                {
                    segment.Add(string.Create(CultureInfo.InvariantCulture, $"{F(X(i))},{F(Y(v))}"));
                }
                else
                {
                    FlushSegment();
                }
            }
            FlushSegment();

            foreach (var (idx, v) in present)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<circle cx=\"{F(X(idx))}\" cy=\"{F(Y(v))}\" r=\"2\" fill=\"{color}\"/>");
            }
        }

        // Legend across the top (wraps as needed).
        double lx = PlotLeft;
        double lyLegend = 14;
        for (int li = 0; li < lines.Count; li++)
        {
            var color = Palette[li % Palette.Length];
            var name = Escape(Truncate(lines[li].MachineName ?? $"#{lines[li].MachineId}", 16));
            double approxWidth = 22 + (name.Length * 5.2);
            if (lx + approxWidth > PlotRight)
            {
                lx = PlotLeft;
                lyLegend += 12;
            }
            sb.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{F(lx)}\" y1=\"{F(lyLegend)}\" x2=\"{F(lx + 14)}\" y2=\"{F(lyLegend)}\" stroke=\"{color}\" stroke-width=\"2\"/>");
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{F(lx + 18)}\" y=\"{F(lyLegend + 3)}\" font-size=\"9\" fill=\"{TextColor}\">{name}</text>");
            lx += approxWidth;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static double Select(FpyKpi kpi, FpyFlavor flavor) => flavor switch
    {
        FpyFlavor.Aoi => kpi.FpyAoiPercent,
        FpyFlavor.AfterRepair => kpi.FpyAfterRepairPercent,
        _ => kpi.FpyDiagnosticPercent,
    };

    private static void Guide(StringBuilder sb, Func<double, double> y, double threshold, double yMin, double yMax, string color)
    {
        if (threshold <= yMin || threshold >= yMax)
        {
            return;
        }
        double ty = y(threshold);
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1=\"{F(PlotLeft)}\" y1=\"{F(ty)}\" x2=\"{F(PlotRight)}\" y2=\"{F(ty)}\" stroke=\"{color}\" stroke-width=\"0.75\" stroke-dasharray=\"3 3\" opacity=\"0.5\"/>");
    }

    private static string Line(double x1, double y1, double x2, double y2, string color, double width) =>
        string.Create(CultureInfo.InvariantCulture,
            $"<line x1=\"{x1:0.##}\" y1=\"{y1:0.##}\" x2=\"{x2:0.##}\" y2=\"{y2:0.##}\" stroke=\"{color}\" stroke-width=\"{width:0.##}\"/>");

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "\u2026");

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal)
         .Replace("\"", "&quot;", StringComparison.Ordinal);
}
