using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Unit tests for <see cref="TrendChartReport"/> (CR3 of
/// docs/phase-2.md §7.3). Covers metric categorisation
/// (panel / card / tested-object), bucket decomposition,
/// FPY / DPMO / Cp / Cpk numeric parity, and the "null bucket" gap
/// semantics for buckets without enough data.
/// </summary>
public sealed class TrendChartReportTests
{
    private static readonly DateTimeOffset WindowStart =
        new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd =
        new(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly SourceDescriptor _descriptor = new(
        "fake", "Fake", "5.0", Capabilities.PinLevel);

    [Fact]
    public async Task Empty_Metrics_Rejected()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Hour6,
            Metrics: Array.Empty<TrendMetric>());
        await Assert.ThrowsAsync<ArgumentException>(
            () => TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_Bucket_Rejected()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: (TimeBucket)99,
            Metrics: new[] { TrendMetric.PanelCount });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    [Fact]
    public async Task Cp_Requires_Both_Tolerances()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[] { TrendMetric.Cp },
            DeviationAxis: DeviationAxis.DeltaX,
            LowerTolerance: -10.0);
        await Assert.ThrowsAsync<ArgumentException>(
            () => TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    [Fact]
    public async Task Cpk_Requires_DeviationAxis()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[] { TrendMetric.Cpk });
        await Assert.ThrowsAsync<ArgumentException>(
            () => TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    [Fact]
    public async Task Inverted_Tolerances_Rejected()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[] { TrendMetric.Cp },
            DeviationAxis: DeviationAxis.DeltaX,
            LowerTolerance: 10.0,
            UpperTolerance: -10.0);
        await Assert.ThrowsAsync<ArgumentException>(
            () => TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None));
    }

    [Fact]
    public async Task Buckets_Cover_Window_And_Are_Ordered_Chronologically()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Hour6,
            Metrics: new[] { TrendMetric.PanelCount });

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        // 24h / 6h = 4 buckets.
        Assert.Equal(4, result.Buckets.Count);
        Assert.Equal(WindowStart, result.Buckets[0].StartUtc);
        Assert.Equal(WindowEnd, result.Buckets[^1].EndUtcExclusive);
        for (var i = 0; i + 1 < result.Buckets.Count; i++)
        {
            Assert.Equal(result.Buckets[i].EndUtcExclusive, result.Buckets[i + 1].StartUtc);
        }
    }

    [Fact]
    public async Task PanelCount_And_Fpy_Compute_Per_Bucket()
    {
        // 3 panels in bucket 0 (hour 0-6): 2 good AOI (status 1) + 1 faulty (status -1).
        // 2 panels in bucket 3 (hour 18-24): 1 good repaired (status 3) + 1 good dummy (status 2).
        var panels = new[]
        {
            Panel(1, WindowStart.AddHours(1), status: 1),
            Panel(2, WindowStart.AddHours(2), status: 1),
            Panel(3, WindowStart.AddHours(3), status: -1),
            Panel(4, WindowStart.AddHours(19), status: 3),
            Panel(5, WindowStart.AddHours(20), status: 2),
        };
        var source = new FakeAoiSource(_descriptor) { SeededPanels = panels };
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Hour6,
            Metrics: new[] { TrendMetric.PanelCount, TrendMetric.FpyAoi, TrendMetric.FpyAfterRepair });

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Equal(4, result.Buckets.Count);
        // Bucket 0: 3 panels, all inspected. FpyAoi = 2/3 → 66.666...,
        // FpyAR = 2/3 too (the -1 panel is still faulty after repair).
        Assert.Equal(3d, result.Buckets[0].Values[TrendMetric.PanelCount]);
        Assert.NotNull(result.Buckets[0].Values[TrendMetric.FpyAoi]);
        Assert.Equal(200d / 3d, result.Buckets[0].Values[TrendMetric.FpyAoi]!.Value, precision: 10);
        Assert.Equal(200d / 3d, result.Buckets[0].Values[TrendMetric.FpyAfterRepair]!.Value, precision: 10);
        // Bucket 1, 2: no panels → PanelCount = 0, FPY = null (chart draws gap).
        Assert.Equal(0d, result.Buckets[1].Values[TrendMetric.PanelCount]);
        Assert.Null(result.Buckets[1].Values[TrendMetric.FpyAoi]);
        Assert.Null(result.Buckets[2].Values[TrendMetric.FpyAoi]);
        // Bucket 3: 2 panels (status 3 + status 2), both inspected.
        // FpyAoi = 0/2 = 0 (neither is a status=1 first-pass good).
        // FpyAR = 2/2 = 100 (GoodDummyOnly + GoodRepaired both count).
        Assert.Equal(2d, result.Buckets[3].Values[TrendMetric.PanelCount]);
        Assert.Equal(0d, result.Buckets[3].Values[TrendMetric.FpyAoi]!.Value, precision: 10);
        Assert.Equal(100d, result.Buckets[3].Values[TrendMetric.FpyAfterRepair]!.Value, precision: 10);
    }

    [Fact]
    public async Task BoardCount_Streams_Cards_Only()
    {
        var cards = new[]
        {
            Card(1, WindowStart.AddHours(2)),
            Card(2, WindowStart.AddHours(3)),
            Card(3, WindowStart.AddHours(20)),
        };
        var source = new FakeAoiSource(_descriptor) { SeededCards = cards };
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Hour12,
            Metrics: new[] { TrendMetric.BoardCount });

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Equal(2, result.Buckets.Count);
        Assert.Equal(2d, result.Buckets[0].Values[TrendMetric.BoardCount]);
        Assert.Equal(1d, result.Buckets[1].Values[TrendMetric.BoardCount]);
    }

    [Fact]
    public async Task Dpmo_Metrics_Follow_Numerator_Semantics()
    {
        // 4 opportunities in bucket 0:
        //   - 2 clean components (Error_Table = 0, Error_Table_AR = 0)
        //   - 1 component with bit 1 set AOI-only (dummy defect)
        //   - 1 component with bit 2 set in AR too (real defect)
        // Expected DpmoAoi = 1e6 * 2 / 4 = 500_000
        // Expected DpmoReal = 1e6 * 1 / 4 = 250_000
        // Expected DpmoDummy = 1e6 * 1 / 4 = 250_000
        var rows = new[]
        {
            Obj(1, WindowStart.AddHours(1), 0x01, errorTable: 0, errorTableAr: 0),
            Obj(2, WindowStart.AddHours(1), 0x01, errorTable: 0, errorTableAr: 0),
            Obj(3, WindowStart.AddHours(1), 0x01, errorTable: 0b01, errorTableAr: 0),
            Obj(4, WindowStart.AddHours(1), 0x01, errorTable: 0b10, errorTableAr: 0b10),
        };
        var source = new FakeAoiSource(_descriptor) { SeededTestedObjects = rows };
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[] { TrendMetric.DpmoAoi, TrendMetric.DpmoReal, TrendMetric.DpmoDummy },
            Opportunity: DpmoOpportunity.Components);

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Single(result.Buckets);
        Assert.Equal(500_000d, result.Buckets[0].Values[TrendMetric.DpmoAoi]);
        Assert.Equal(250_000d, result.Buckets[0].Values[TrendMetric.DpmoReal]);
        Assert.Equal(250_000d, result.Buckets[0].Values[TrendMetric.DpmoDummy]);
    }

    [Fact]
    public async Task Cp_Matches_Hand_Calculation_Over_Deviation_Sample()
    {
        // 5 delta-X samples in bucket 0: -2, -1, 0, 1, 2 → mean 0, sample stddev = sqrt(2.5).
        // Tolerance ±3 → Cp = 6 / (6*sqrt(2.5)) = 1/sqrt(2.5) ≈ 0.63246.
        var rows = new[]
        {
            Obj(1, WindowStart.AddHours(1), 0x01, dxUm: -2.0),
            Obj(2, WindowStart.AddHours(1), 0x01, dxUm: -1.0),
            Obj(3, WindowStart.AddHours(1), 0x01, dxUm: 0.0),
            Obj(4, WindowStart.AddHours(1), 0x01, dxUm: 1.0),
            Obj(5, WindowStart.AddHours(1), 0x01, dxUm: 2.0),
        };
        var source = new FakeAoiSource(_descriptor) { SeededTestedObjects = rows };
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[] { TrendMetric.Cp, TrendMetric.Cpk },
            Opportunity: DpmoOpportunity.Components,
            DeviationAxis: DeviationAxis.DeltaX,
            LowerTolerance: -3.0,
            UpperTolerance: 3.0);

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Single(result.Buckets);
        var expectedCp = 6d / (6d * Math.Sqrt(2.5));
        Assert.Equal(expectedCp, result.Buckets[0].Values[TrendMetric.Cp]!.Value, precision: 10);
        // Symmetric sample centred on 0 → Cpk = Cp.
        Assert.Equal(expectedCp, result.Buckets[0].Values[TrendMetric.Cpk]!.Value, precision: 10);
    }

    [Fact]
    public async Task Cp_Null_When_Fewer_Than_Two_Samples()
    {
        var rows = new[]
        {
            Obj(1, WindowStart.AddHours(1), 0x01, dxUm: 0.5),
        };
        var source = new FakeAoiSource(_descriptor) { SeededTestedObjects = rows };
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[] { TrendMetric.Cp },
            Opportunity: DpmoOpportunity.Components,
            DeviationAxis: DeviationAxis.DeltaX,
            LowerTolerance: -1.0,
            UpperTolerance: 1.0);

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Single(result.Buckets);
        Assert.Null(result.Buckets[0].Values[TrendMetric.Cp]);
    }

    [Fact]
    public async Task Metrics_Deduplicated_But_Preserve_Order()
    {
        var source = new FakeAoiSource(_descriptor);
        var filter = new TrendFilter(
            Window: new DateRange(WindowStart, WindowEnd),
            Bucket: TimeBucket.Day,
            Metrics: new[]
            {
                TrendMetric.PanelCount,
                TrendMetric.FpyAoi,
                TrendMetric.PanelCount, // duplicate — dropped
                TrendMetric.BoardCount,
            });

        var result = await TrendChartReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.Equal(3, result.Series.Count);
        Assert.Equal(TrendMetric.PanelCount, result.Series[0].Metric);
        Assert.Equal(TrendMetric.FpyAoi, result.Series[1].Metric);
        Assert.Equal(TrendMetric.BoardCount, result.Series[2].Metric);
    }

    // ------------------ Helpers ------------------

    private static PanelRow Panel(int panelId, DateTimeOffset when, int status)
        => new(
            PanelId: panelId,
            MachineId: 10,
            LaneNumber: 1,
            PanelBarCode: "BC" + panelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PanelNumericDate: (int)when.ToUnixTimeSeconds(),
            NbOfValidCards: 4,
            TestTime: 12.0,
            PanelStatus: status,
            AnomalyBr: 0,
            AnomalyAr: 0,
            HasBeenReviewed: false,
            NbOfTestedObject: 100,
            NbOfErrorObject: status is 1 or 2 or 3 or 0 ? 0 : 1,
            OperatorId: null,
            ProductId: 100,
            RecipeId: 1000);

    private static CardRow Card(int cardId, DateTimeOffset when)
        => new(
            PanelId: cardId,
            CardIdOnPanel: 1,
            CardStatus: 1,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 50,
            NbOfErrorObject: 0,
            MachineId: 10,
            ProductId: 100,
            PanelNumericDate: (int)when.ToUnixTimeSeconds());

    private static TestedObjectRow Obj(
        int objectId,
        DateTimeOffset when,
        int objectTypeId,
        long errorTable = 0,
        long errorTableAr = 0,
        double? dxUm = null)
        => new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: objectId,
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: 10,
            ProductId: 100,
            PanelNumericDate: (int)when.ToUnixTimeSeconds(),
            Topology: "R" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PartNumberName: null,
            JedecName: null,
            DeltaXUm: dxUm);
}
