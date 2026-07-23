using Nieweb.DataSources;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Unit tests for <see cref="DeviationChartReport"/> (CR2 of
/// docs/phase-2.md §7.3). Covers axis projection, opportunity
/// filtering, ±3σ overlay math, out-of-tolerance counting, and the
/// zero-sample / degenerate-sample fallbacks.
/// </summary>
public sealed class DeviationChartReportTests
{
    private static readonly DateTimeOffset WindowStart =
        new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd =
        new(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_Source_Returns_Empty_Bins_And_Nan_Stats()
    {
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None));
        var filter = BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components);

        var result = await DeviationChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Equal(0, result.SampleCount);
        Assert.True(double.IsNaN(result.Mean));
        Assert.Equal(0, result.StdDev);
        Assert.True(double.IsNaN(result.PlusThreeSigma));
        Assert.True(double.IsNaN(result.MinusThreeSigma));
        Assert.True(double.IsNaN(result.Min));
        Assert.True(double.IsNaN(result.Max));
        Assert.Equal(filter.BinCount, result.Bins.Count);
        Assert.All(result.Bins, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public async Task Bins_Cover_Sample_Range_And_Sum_Equals_Sample_Count()
    {
        // 10 rows with deltaX = 0, 10, 20, ..., 90 µm. All components.
        var rows = Enumerable.Range(0, 10)
            .Select(i => ComponentRow(objectId: i, dxUm: i * 10.0))
            .ToArray();
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = rows,
        };
        var filter = BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components, binCount: 10);

        var result = await DeviationChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Equal(10, result.SampleCount);
        // Bin[0] lower bound = min (0), bin[last] upper bound = max (90).
        Assert.Equal(0.0, result.Bins[0].LowerBound);
        Assert.Equal(90.0, result.Bins[^1].UpperBound);
        var total = result.Bins.Sum(b => b.Count);
        Assert.Equal(10L, total);
        // Every row falls in a different bin (uniform layout).
        Assert.All(result.Bins, b => Assert.Equal(1L, b.Count));
    }

    [Fact]
    public async Task Mean_And_StdDev_Match_Known_Sample()
    {
        // Delta-Y = -2, -1, 0, 1, 2 → mean 0, sample stddev = sqrt(2.5) ≈ 1.58114.
        var values = new[] { -2.0, -1.0, 0.0, 1.0, 2.0 };
        var rows = values.Select((v, i) => ComponentRow(objectId: i, dyUm: v)).ToArray();
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = rows,
        };

        var result = await DeviationChartReport.Instance.RunAsync(
            source,
            BuildFilter(DeviationAxis.DeltaY, DpmoOpportunity.Components),
            CancellationToken.None);

        Assert.Equal(5, result.SampleCount);
        Assert.Equal(0.0, result.Mean, precision: 10);
        Assert.Equal(Math.Sqrt(2.5), result.StdDev, precision: 10);
        Assert.Equal(3 * Math.Sqrt(2.5), result.PlusThreeSigma, precision: 10);
        Assert.Equal(-3 * Math.Sqrt(2.5), result.MinusThreeSigma, precision: 10);
    }

    [Fact]
    public async Task Opportunity_Filter_Excludes_Wrong_Object_Type()
    {
        // 3 component rows (dx=1,2,3) + 3 paste rows (dx=100,200,300).
        // Opportunity=Components must ignore paste rows entirely.
        var rows = new List<TestedObjectRow>();
        for (var i = 0; i < 3; i++)
        {
            rows.Add(ComponentRow(objectId: i, dxUm: i + 1));
        }
        for (var i = 0; i < 3; i++)
        {
            rows.Add(PasteRow(objectId: 100 + i, dxUm: (i + 1) * 100));
        }
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = rows,
        };

        var result = await DeviationChartReport.Instance.RunAsync(
            source,
            BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components),
            CancellationToken.None);

        Assert.Equal(3, result.SampleCount);
        Assert.Equal(1.0, result.Min);
        Assert.Equal(3.0, result.Max);
        Assert.Equal(2.0, result.Mean, precision: 10);
    }

    [Fact]
    public async Task Out_Of_Tolerance_Counts_Both_Sides()
    {
        // Rows at -10, -5, 0, 5, 10. Tolerance ±6 → 2 out-of-tolerance
        // (the -10 and +10).
        var values = new[] { -10.0, -5.0, 0.0, 5.0, 10.0 };
        var rows = values.Select((v, i) => ComponentRow(objectId: i, dxUm: v)).ToArray();
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = rows,
        };
        var filter = BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components) with
        {
            LowerTolerance = -6.0,
            UpperTolerance = 6.0,
        };

        var result = await DeviationChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Equal(2L, result.OutOfToleranceCount);
        Assert.Equal(-6.0, result.LowerTolerance);
        Assert.Equal(6.0, result.UpperTolerance);
    }

    [Fact]
    public async Task Null_Delta_Value_Is_Skipped()
    {
        // One row with null DeltaX, two with real values. Sample count = 2.
        var rows = new[]
        {
            ComponentRow(objectId: 1, dxUm: null),
            ComponentRow(objectId: 2, dxUm: 10.0),
            ComponentRow(objectId: 3, dxUm: 20.0),
        };
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = rows,
        };

        var result = await DeviationChartReport.Instance.RunAsync(
            source,
            BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components),
            CancellationToken.None);

        Assert.Equal(2, result.SampleCount);
        Assert.Equal(15.0, result.Mean, precision: 10);
    }

    [Fact]
    public async Task Degenerate_Sample_All_Identical_Uses_Unit_Width()
    {
        var rows = Enumerable.Range(0, 4)
            .Select(i => ComponentRow(objectId: i, dxUm: 42.0))
            .ToArray();
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = rows,
        };

        var result = await DeviationChartReport.Instance.RunAsync(
            source,
            BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components, binCount: 4),
            CancellationToken.None);

        Assert.Equal(42.0, result.Min);
        Assert.Equal(42.0, result.Max);
        // All samples land in bin 0 because bin 0 spans [42, 42.25).
        Assert.Equal(4L, result.Bins[0].Count);
        Assert.Equal(0L, result.Bins[1].Count);
        Assert.Equal(0L, result.Bins[2].Count);
        Assert.Equal(0L, result.Bins[3].Count);
        // Stddev = 0 (identical samples) → 3σ overlays are exactly the mean.
        Assert.Equal(0.0, result.StdDev, precision: 10);
        Assert.Equal(42.0, result.PlusThreeSigma, precision: 10);
        Assert.Equal(42.0, result.MinusThreeSigma, precision: 10);
    }

    [Fact]
    public async Task Invalid_BinCount_Rejected()
    {
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None));
        var filter = BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components) with { BinCount = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => DeviationChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    [Fact]
    public async Task Inverted_Tolerance_Rejected()
    {
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None));
        var filter = BuildFilter(DeviationAxis.DeltaX, DpmoOpportunity.Components) with
        {
            LowerTolerance = 5.0,
            UpperTolerance = -5.0,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => DeviationChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    private static readonly (DeviationAxis Axis, double Expected)[] _allAxes =
    [
        (DeviationAxis.DeltaX, 1.0),
        (DeviationAxis.DeltaY, 2.0),
        (DeviationAxis.DeltaTheta, 3.0),
        (DeviationAxis.DeltaThickness, 4.0),
        (DeviationAxis.DeltaSurface, 5.0),
    ];

    [Fact]
    public async Task All_Axes_Are_Projected()
    {
        // One row with every Delta_* populated. Each axis run must
        // return a mean equal to that row's value on that axis.
        var row = ComponentRow(objectId: 1, dxUm: 1.0, dyUm: 2.0, dthetaDeg: 3.0, dzUm: 4.0, dsRatio: 5.0);
        var source = new FakeAoiSource(new SourceDescriptor("fake", "Fake", "5.0", Capabilities.None))
        {
            SeededTestedObjects = new[] { row },
        };

        foreach (var (axis, expected) in _allAxes)
        {
            var result = await DeviationChartReport.Instance.RunAsync(
                source,
                BuildFilter(axis, DpmoOpportunity.Components, binCount: 1),
                CancellationToken.None);
            Assert.Equal(1, result.SampleCount);
            Assert.Equal(expected, result.Mean, precision: 10);
        }
    }

    // ---- Helpers ----

    private static DeviationFilter BuildFilter(
        DeviationAxis axis,
        DpmoOpportunity opportunity,
        int binCount = 20)
        => new(
            Window: new DateRange(WindowStart, WindowEnd),
            Axis: axis,
            Opportunity: opportunity,
            BinCount: binCount);

    private static TestedObjectRow ComponentRow(
        int objectId,
        double? dxUm = null,
        double? dyUm = null,
        double? dthetaDeg = null,
        double? dzUm = null,
        double? dsRatio = null)
        => new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: objectId,
            ObjectTypeId: 0x01,  // Component
            ErrorTable: 0,
            ErrorTableAr: 0,
            Status: 0,
            MachineId: 10,
            ProductId: 100,
            PanelNumericDate: (int)WindowStart.AddHours(1).ToUnixTimeSeconds(),
            Topology: "R" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PartNumberName: "RES-10K",
            JedecName: "0603",
            DeltaXUm: dxUm,
            DeltaYUm: dyUm,
            DeltaThetaDeg: dthetaDeg,
            DeltaThicknessUm: dzUm,
            DeltaSurface: dsRatio);

    private static TestedObjectRow PasteRow(
        int objectId,
        double? dxUm = null)
        => new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: objectId,
            ObjectTypeId: 0x10,  // Paste pad
            ErrorTable: 0,
            ErrorTableAr: 0,
            Status: 0,
            MachineId: 10,
            ProductId: 100,
            PanelNumericDate: (int)WindowStart.AddHours(1).ToUnixTimeSeconds(),
            Topology: "PAD" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PartNumberName: null,
            JedecName: null,
            DeltaXUm: dxUm);
}
