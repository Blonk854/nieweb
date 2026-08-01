using System.Globalization;
using System.Text;

using Nieweb.Reports;

namespace Nieweb.Pdf;

/// <summary>
/// Renders the per-machine FPY bar chart (one bar per AOI machine,
/// coloured by green / amber / red band, with dashed overall-FPY and
/// threshold reference lines) to a self-contained SVG string that
/// mirrors the SPA's ECharts <c>FpyBarChart</c>. QuestPDF embeds it
/// natively via <c>IContainer.Svg(string)</c>, so the Panel Yield PDF
/// carries the same visual the browser shows — no rasterisation and no
/// client round-trip.
/// </summary>
internal static class FpyChartSvg
{
    // Band palette mirrors FPY_BAND_COLORS in
    // src/Nieweb.Web/src/charts/fpyThresholds.ts.
    private const string GreenColor = "#2f9e44";
    private const string AmberColor = "#f08c00";
    private const string RedColor = "#e03131";
    private const string OverallColor = "#495057";
    private const string AxisColor = "#adb5bd";
    private const string GridColor = "#e9ecef";
    private const string TextColor = "#495057";

    // Default site thresholds (docs/phase-1-mvp.md §7.5 F5), matching
    // DEFAULT_FPY_THRESHOLDS in fpyThresholds.ts.
    private const double GreenThreshold = 99.5;
    private const double AmberThreshold = 98.0;

    // Canvas geometry (viewBox units) — same frame as ParetoChartSvg.
    private const double W = 960;
    private const double H = 300;
    private const double PlotLeft = 54;
    private const double PlotRight = 906;
    private const double PlotTop = 34;
    private const double PlotBottom = 228;

    /// <summary>
    /// Build the SVG markup for the per-machine FPY breakdown in
    /// <paramref name="result"/>. Bars use the report's machine order
    /// (ascending machine id). Returns an empty <c>&lt;svg/&gt;</c> when
    /// there are no machine rows.
    /// </summary>
    public static string Build(PanelYieldResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = result.ByMachine;
        var sb = new StringBuilder(4096);
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(W)} {F(H)}\" font-family=\"Helvetica, Arial, sans-serif\">");

        if (rows.Count == 0)
        {
            sb.Append("</svg>");
            return sb.ToString();
        }

        const double plotWidth = PlotRight - PlotLeft;
        const double plotHeight = PlotBottom - PlotTop;
        int count = rows.Count;
        double slot = plotWidth / count;
        double barWidth = Math.Min(slot * 0.62, 48);

        // Y range mirrors the SPA zoom: when every machine is at least
        // in the amber band, zoom in so small differences are visible;
        // otherwise keep the full 0..100 so red severity shows.
        double minValue = double.PositiveInfinity;
        foreach (var m in rows)
        {
            double v = m.Kpi.FpyPercent;
            if (double.IsFinite(v) && v < minValue)
            {
                minValue = v;
            }
        }
        if (!double.IsFinite(minValue))
        {
            minValue = 0;
        }
        double yMin = minValue >= AmberThreshold ? Math.Max(0, Math.Floor(minValue) - 1) : 0;
        const double yMax = 100;
        double range = yMax - yMin <= 0 ? 1 : yMax - yMin;

        double Y(double v) => PlotBottom - ((v - yMin) / range) * plotHeight;

        // Horizontal gridlines with left-hand % labels.
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

        // Axis frame (left + bottom).
        sb.Append(Line(PlotLeft, PlotTop, PlotLeft, PlotBottom, AxisColor, 1));
        sb.Append(Line(PlotLeft, PlotBottom, PlotRight, PlotBottom, AxisColor, 1));

        // Bars, coloured by band.
        for (int i = 0; i < count; i++)
        {
            double v = double.IsFinite(rows[i].Kpi.FpyPercent) ? rows[i].Kpi.FpyPercent : 0;
            double clamped = Math.Clamp(v, yMin, yMax);
            double x = PlotLeft + (i * slot) + ((slot - barWidth) / 2.0);
            double top = Y(clamped);
            double height = Math.Max(0, PlotBottom - top);
            sb.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{F(x)}\" y=\"{F(top)}\" width=\"{F(barWidth)}\" height=\"{F(height)}\" fill=\"{BandColor(v)}\"/>");
        }

        // Reference lines. Green label sits above its line and amber
        // below its own line so the two never stack (they diverge even
        // when the thresholds are only a fraction of a percent apart —
        // the collision the ECharts markLine labels used to produce).
        Threshold(sb, Y, GreenThreshold, yMin, yMax, GreenColor, $"Green {Pct(GreenThreshold)}%", below: false);
        Threshold(sb, Y, AmberThreshold, yMin, yMax, AmberColor, $"Amber {Pct(AmberThreshold)}%", below: true);

        // Overall FPY (dashed), labelled on the right so it clears the
        // left-anchored threshold labels.
        double overall = result.Overall.FpyPercent;
        if (double.IsFinite(overall) && overall > yMin && overall < yMax)
        {
            double oy = Y(overall);
            sb.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{F(PlotLeft)}\" y1=\"{F(oy)}\" x2=\"{F(PlotRight)}\" y2=\"{F(oy)}\" stroke=\"{OverallColor}\" stroke-width=\"2\" stroke-dasharray=\"6 4\"/>");
            double ly = Math.Max(oy - 4, PlotTop + 8);
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{F(PlotRight - 4)}\" y=\"{F(ly)}\" text-anchor=\"end\" font-size=\"9\" fill=\"{OverallColor}\">Overall {overall.ToString("0.00", CultureInfo.InvariantCulture)}%</text>");
        }

        // X-axis machine labels — rotate when crowded, thin when dense.
        int maxLen = 0;
        foreach (var m in rows)
        {
            int len = LabelOf(m).Length;
            if (len > maxLen)
            {
                maxLen = len;
            }
        }
        bool rotate = count > 6 || maxLen > 8;
        int labelEvery = count > 32 ? (int)Math.Ceiling(count / 32.0) : 1;
        for (int i = 0; i < count; i++)
        {
            if (i % labelEvery != 0)
            {
                continue;
            }
            double cx = PlotLeft + (i * slot) + (slot / 2.0);
            string label = Escape(Truncate(LabelOf(rows[i]), 22));
            if (rotate)
            {
                double ly = PlotBottom + 10;
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{F(cx)}\" y=\"{F(ly)}\" text-anchor=\"end\" font-size=\"8\" fill=\"{TextColor}\" transform=\"rotate(-40 {F(cx)} {F(ly)})\">{label}</text>");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{F(cx)}\" y=\"{F(PlotBottom + 14)}\" text-anchor=\"middle\" font-size=\"8\" fill=\"{TextColor}\">{label}</text>");
            }
        }

        // Legend: band swatches + dashed overall marker.
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{F(PlotLeft)}\" y=\"12\" width=\"10\" height=\"10\" fill=\"{GreenColor}\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(PlotLeft + 14)}\" y=\"21\" font-size=\"9\" fill=\"{TextColor}\">Green</text>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{F(PlotLeft + 52)}\" y=\"12\" width=\"10\" height=\"10\" fill=\"{AmberColor}\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(PlotLeft + 66)}\" y=\"21\" font-size=\"9\" fill=\"{TextColor}\">Amber</text>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{F(PlotLeft + 108)}\" y=\"12\" width=\"10\" height=\"10\" fill=\"{RedColor}\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(PlotLeft + 122)}\" y=\"21\" font-size=\"9\" fill=\"{TextColor}\">Red</text>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1=\"{F(PlotLeft + 152)}\" y1=\"17\" x2=\"{F(PlotLeft + 176)}\" y2=\"17\" stroke=\"{OverallColor}\" stroke-width=\"2\" stroke-dasharray=\"6 4\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(PlotLeft + 180)}\" y=\"21\" font-size=\"9\" fill=\"{TextColor}\">Overall FPY</text>");

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Threshold(
        StringBuilder sb, Func<double, double> y, double threshold,
        double yMin, double yMax, string color, string label, bool below)
    {
        if (threshold <= yMin || threshold >= yMax)
        {
            return;
        }
        double ty = y(threshold);
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1=\"{F(PlotLeft)}\" y1=\"{F(ty)}\" x2=\"{F(PlotRight)}\" y2=\"{F(ty)}\" stroke=\"{color}\" stroke-width=\"1\" stroke-dasharray=\"3 3\"/>");
        double ly = below ? Math.Min(ty + 11, PlotBottom - 2) : Math.Max(ty - 4, PlotTop + 8);
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(PlotLeft + 4)}\" y=\"{F(ly)}\" text-anchor=\"start\" font-size=\"9\" fill=\"{color}\">{Escape(label)}</text>");
    }

    private static string BandColor(double fpyPercent)
    {
        if (!double.IsFinite(fpyPercent) || fpyPercent < AmberThreshold)
        {
            return RedColor;
        }
        return fpyPercent >= GreenThreshold ? GreenColor : AmberColor;
    }

    private static string LabelOf(PanelYieldByMachine m) =>
        string.IsNullOrEmpty(m.MachineName)
            ? string.Create(CultureInfo.InvariantCulture, $"#{m.MachineId}")
            : m.MachineName;

    private static string Line(double x1, double y1, double x2, double y2, string color, double width) =>
        string.Create(
            CultureInfo.InvariantCulture,
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
