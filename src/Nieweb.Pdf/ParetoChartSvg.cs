using System.Globalization;
using System.Text;
using Nieweb.Reports;

namespace Nieweb.Pdf;

/// <summary>
/// Renders a <see cref="ParetoResult"/> to a self-contained SVG string
/// (bars +, in count mode, a cumulative-percent line and dashed
/// vital-few threshold) that mirrors the SPA's ECharts Pareto chart.
/// </summary>
internal static class ParetoChartSvg
{
    private const string VitalFew = "#c92a2a";
    private const string TrivialMany = "#868e96";
    private const string OthersColor = "#495057";
    private const string CumulativeColor = "#1c7ed6";
    private const string AxisColor = "#adb5bd";
    private const string GridColor = "#e9ecef";
    private const string TextColor = "#495057";

    private const double W = 960;
    private const double H = 300;
    private const double PlotLeft = 54;
    private const double PlotRight = 906;
    private const double PlotTop = 34;
    private const double PlotBottom = 228;

    private readonly record struct Bar(string Label, double Magnitude, double Cumulative, string Color);

    public static string Build(ParetoResult result, double vitalFewThresholdPercent)
    {
        var showCumulative = ParetoPresentation.ShowCumulative(result.Weight);
        var showVitalFew = ParetoPresentation.ShowVitalFew(result.Weight);
        var bars = new List<Bar>(result.Rows.Count + 1);
        foreach (var r in result.Rows)
        {
            bars.Add(new Bar(
                LabelOf(r, result.Axis),
                ParetoPresentation.BarMagnitude(result.Weight, r),
                r.CumulativePercent,
                showVitalFew && r.IsVitalFew ? VitalFew : TrivialMany));
        }
        if (result.OthersBucket is { } others)
        {
            bars.Add(new Bar(
                "Others",
                ParetoPresentation.BarMagnitude(result.Weight, others),
                others.CumulativePercent,
                OthersColor));
        }

        var sb = new StringBuilder(4096);
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(W)} {F(H)}\" font-family=\"Helvetica, Arial, sans-serif\">");

        if (bars.Count == 0)
        {
            sb.Append("</svg>");
            return sb.ToString();
        }

        const double plotWidth = PlotRight - PlotLeft;
        const double plotHeight = PlotBottom - PlotTop;
        int count = bars.Count;
        double slot = plotWidth / count;
        double barWidth = slot * 0.62;

        double maxMagnitude = 1;
        foreach (var b in bars)
        {
            if (b.Magnitude > maxMagnitude)
            {
                maxMagnitude = b.Magnitude;
            }
        }

        double YMag(double v) => PlotBottom - (v / maxMagnitude) * plotHeight;
        double YPct(double p) => PlotBottom - (p / 100.0) * plotHeight;

        for (int g = 0; g <= 4; g++)
        {
            double frac = g / 4.0;
            double y = PlotBottom - frac * plotHeight;
            sb.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{F(PlotLeft)}\" y1=\"{F(y)}\" x2=\"{F(PlotRight)}\" y2=\"{F(y)}\" stroke=\"{GridColor}\" stroke-width=\"0.75\"/>");
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{F(PlotLeft - 5)}\" y=\"{F(y + 3)}\" text-anchor=\"end\" font-size=\"9\" fill=\"{TextColor}\">{F0(maxMagnitude * frac)}</text>");
            if (showCumulative)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{F(PlotRight + 5)}\" y=\"{F(y + 3)}\" text-anchor=\"start\" font-size=\"9\" fill=\"{TextColor}\">{F0(100 * frac)}%</text>");
            }
        }

        sb.Append(Line(PlotLeft, PlotTop, PlotLeft, PlotBottom, AxisColor, 1));
        if (showCumulative)
        {
            sb.Append(Line(PlotRight, PlotTop, PlotRight, PlotBottom, AxisColor, 1));
        }
        sb.Append(Line(PlotLeft, PlotBottom, PlotRight, PlotBottom, AxisColor, 1));

        for (int i = 0; i < count; i++)
        {
            var b = bars[i];
            double x = PlotLeft + (i * slot) + ((slot - barWidth) / 2.0);
            double top = YMag(b.Magnitude);
            double height = PlotBottom - top;
            sb.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{F(x)}\" y=\"{F(top)}\" width=\"{F(barWidth)}\" height=\"{F(height)}\" fill=\"{b.Color}\"/>");
        }

        if (showCumulative)
        {
            if (vitalFewThresholdPercent > 0 && vitalFewThresholdPercent < 100)
            {
                double ty = YPct(vitalFewThresholdPercent);
                sb.Append(CultureInfo.InvariantCulture,
                    $"<line x1=\"{F(PlotLeft)}\" y1=\"{F(ty)}\" x2=\"{F(PlotRight)}\" y2=\"{F(ty)}\" stroke=\"{VitalFew}\" stroke-width=\"1\" stroke-dasharray=\"5 4\"/>");
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{F(PlotRight - 4)}\" y=\"{F(ty - 4)}\" text-anchor=\"end\" font-size=\"9\" fill=\"{VitalFew}\">{F0(vitalFewThresholdPercent)}%</text>");
            }

            var points = new StringBuilder(count * 12);
            for (int i = 0; i < count; i++)
            {
                double cx = PlotLeft + (i * slot) + (slot / 2.0);
                double cy = YPct(bars[i].Cumulative);
                if (i > 0)
                {
                    points.Append(' ');
                }
                points.Append(CultureInfo.InvariantCulture, $"{F(cx)},{F(cy)}");
            }
            sb.Append(CultureInfo.InvariantCulture,
                $"<polyline points=\"{points}\" fill=\"none\" stroke=\"{CumulativeColor}\" stroke-width=\"1.5\"/>");
            for (int i = 0; i < count; i++)
            {
                double cx = PlotLeft + (i * slot) + (slot / 2.0);
                double cy = YPct(bars[i].Cumulative);
                sb.Append(CultureInfo.InvariantCulture,
                    $"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"2.5\" fill=\"{CumulativeColor}\"/>");
            }
        }

        int maxLen = 0;
        foreach (var b in bars)
        {
            if (b.Label.Length > maxLen)
            {
                maxLen = b.Label.Length;
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
            string label = Escape(Truncate(bars[i].Label, 22));
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

        var seriesCaption = ParetoPresentation.LeftAxisCaption(result.Weight);
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{F(PlotLeft)}\" y=\"12\" width=\"10\" height=\"10\" fill=\"{(showVitalFew ? VitalFew : TrivialMany)}\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(PlotLeft + 14)}\" y=\"21\" font-size=\"9\" fill=\"{TextColor}\">{Escape(seriesCaption)}</text>");
        if (showCumulative)
        {
            sb.Append(Line(PlotLeft + 66, 17, PlotLeft + 86, 17, CumulativeColor, 1.5));
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{F(PlotLeft + 90)}\" y=\"21\" font-size=\"9\" fill=\"{TextColor}\">Cumulative %</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Line(double x1, double y1, double x2, double y2, string color, double width) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"<line x1=\"{x1:0.##}\" y1=\"{y1:0.##}\" x2=\"{x2:0.##}\" y2=\"{y2:0.##}\" stroke=\"{color}\" stroke-width=\"{width:0.##}\"/>");

    private static string LabelOf(ParetoRow r, ParetoAxis axis)
    {
        if (!string.IsNullOrEmpty(r.GroupName))
        {
            return r.GroupName;
        }
        if (string.IsNullOrEmpty(r.GroupKey))
        {
            return "\u2014";
        }
        return axis == ParetoAxis.Defect ? $"bit {r.GroupKey}" : r.GroupKey;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "\u2026");

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string F0(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal)
         .Replace("\"", "&quot;", StringComparison.Ordinal);
}
