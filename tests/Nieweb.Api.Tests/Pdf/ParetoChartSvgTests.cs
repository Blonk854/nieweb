using Nieweb.DataSources;
using Nieweb.Pdf;
using Nieweb.Reports;
using Xunit;

namespace Nieweb.Api.Tests.Pdf;

/// <summary>
/// Unit tests for <see cref="ParetoChartSvg"/> — the native SVG the PDF
/// export embeds. Asserts the structural pieces (bars, cumulative line,
/// markers, dashed vital-few threshold, category labels, legend) rather
/// than exact pixel geometry.
/// </summary>
public sealed class ParetoChartSvgTests
{
    private static readonly SourceDescriptor Source =
        new("postreflow", "Post-reflow", "5.0", Capabilities.None);

    private static readonly DateRange Window = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    private static readonly ParetoAppliedFilters NoFilters =
        new([], [], [], [], [], []);

    private static ParetoResult Result(
        IReadOnlyList<ParetoRow> rows,
        ParetoRow? others = null,
        ParetoAxis axis = ParetoAxis.Defect) =>
        new(
            Source,
            Window,
            axis,
            DpmoNumerator.Real,
            DpmoOpportunity.All,
            ParetoWeight.Count,
            NoFilters,
            new DpmoKpi(300, 300, 100, 333_333),
            rows,
            others);

    private static ParetoRow Row(
        string? key,
        string? name,
        long defects,
        double cumulative,
        bool vitalFew) =>
        new(key, name, defects, defects, 100, 33.3, 0, 0, cumulative, vitalFew);

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }

        return n;
    }

    [Fact]
    public void Build_WithRows_EmitsBarsCumulativeLineAndThreshold()
    {
        var result = Result(
        [
            Row("1", "Missing", 50, 50, vitalFew: true),
            Row("2", "Polarity", 30, 80, vitalFew: true),
            Row("3", "Bridge", 20, 100, vitalFew: false),
        ]);

        var svg = ParetoChartSvg.Build(result, vitalFewThresholdPercent: 80);

        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", svg, StringComparison.Ordinal);
        // 3 bars + 1 legend swatch.
        Assert.Equal(4, Count(svg, "<rect"));
        // One cumulative polyline + one marker per row.
        Assert.Equal(1, Count(svg, "<polyline"));
        Assert.Equal(3, Count(svg, "<circle"));
        // Dashed vital-few threshold present for an interior percentage.
        Assert.Contains("stroke-dasharray", svg, StringComparison.Ordinal);
        // Category label + legend text.
        Assert.Contains(">Missing<", svg, StringComparison.Ordinal);
        Assert.Contains("Cumulative %", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Build_ThresholdAtBoundary_OmitsThresholdLine(double threshold)
    {
        var result = Result([Row("1", "Missing", 50, 100, vitalFew: true)]);

        var svg = ParetoChartSvg.Build(result, threshold);

        Assert.DoesNotContain("stroke-dasharray", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MissingGroupName_UsesBitLabelOnDefectAxis()
    {
        var result = Result(
            [Row("7", name: null, 12, 100, vitalFew: true)],
            axis: ParetoAxis.Defect);

        var svg = ParetoChartSvg.Build(result, vitalFewThresholdPercent: 80);

        Assert.Contains(">bit 7<", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithOthersBucket_AddsOthersBar()
    {
        var result = Result(
            [Row("1", "Missing", 50, 71, vitalFew: true)],
            others: Row(key: null, name: null, 20, 100, vitalFew: false));

        var svg = ParetoChartSvg.Build(result, vitalFewThresholdPercent: 80);

        // 1 data bar + 1 Others bar + 1 legend swatch.
        Assert.Equal(3, Count(svg, "<rect"));
        Assert.Contains(">Others<", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NoRows_ReturnsEmptySvg()
    {
        var result = Result([]);

        var svg = ParetoChartSvg.Build(result, vitalFewThresholdPercent: 80);

        Assert.DoesNotContain("<rect", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<polyline", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", svg, StringComparison.Ordinal);
    }
}
