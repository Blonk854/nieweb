using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="DpmoTrendByLineReport"/>: day / week bucketing,
/// per-line series with gap semantics, all three numerator flavours per
/// point, the card-derived opportunity denominator, paste gating by
/// <see cref="Capabilities.PastePrintMetrics"/>, and numeric parity with
/// <see cref="DpmoTableReport"/> over the same scope.
/// </summary>
public sealed class DpmoTrendByLineReportTests
{
    private static readonly SourceDescriptor _postReflow = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI",
        SchemaVersion: "5.0",
        Caps: Capabilities.PinLevel | Capabilities.IsLastInspectionFilter);

    private static readonly SourceDescriptor _preReflow = new(
        Id: "prereflow",
        DisplayName: "Pre-reflow AOI",
        SchemaVersion: "4.3.1",
        Caps: Capabilities.PastePrintMetrics | Capabilities.FeederAnalytics);

    // Three full UTC days: 2026-01-01 .. 2026-01-04.
    private static readonly DateRange _threeDays = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero));

    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    // OBJECT_TYPE bit codes.
    private const int ComponentType = 0x01;
    private const int PastePadType = 0x10;

    // Defect bit masks (DefectBit / vit-aoi-database skill).
    private const long BitObjectMissing = 1L << 0;
    private const long BitPolarityError = 1L << 1;
    private const long BitSolderJoint = 1L << 2;

    private static int Start => (int)_threeDays.StartEpochSeconds;

    /// <summary>Epoch seconds one hour into day <paramref name="dayIndex"/> of the window.</summary>
    private static int Day(int dayIndex) => Start + (dayIndex * 86_400) + 3_600;

    [Fact]
    public async Task Rejects_NonDayWeek_Bucket()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new DpmoTrendFilter(_threeDays, TimeBucket.Hour6, SiteTimeZone: TimeZoneInfo.Utc);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => DpmoTrendByLineReport.Instance.RunAsync(
                source, filter, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_ReturnsBucketsButNoLines()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new DpmoTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            SkipExclusion: SkipExclusion.Raw);

        var result = await DpmoTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(_postReflow, result.Source);
        Assert.Equal(3, result.Buckets.Count);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task Day_PerLineSeries_Flavours_Gaps_Ordering()
    {
        // Machine 10: day0 100 comp tests + 3 defect bits, day1 100 tests +
        //             1 bit, day2 100 tests + 0 bits.
        // Machine 11: day0 50 tests + 1 bit, day2 50 tests + 0 bits
        //             (no day1 card at all => gap).
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, Day(0), nbTestsOnComp: 100),
                Card(10, Day(1), nbTestsOnComp: 100),
                Card(10, Day(2), nbTestsOnComp: 100),
                Card(11, Day(0), nbTestsOnComp: 50),
                Card(11, Day(2), nbTestsOnComp: 50),
            ],
            SeededTestedObjects =
            [
                // day0 machine 10: missing|polarity (2 bits) + solder (1 bit) = 3
                Obj(10, Day(0) + 1, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing),
                Obj(10, Day(0) + 2, ComponentType, BitSolderJoint, 0),
                // day1 machine 10: 1 bit, fully real
                Obj(10, Day(1) + 1, ComponentType, BitObjectMissing, BitObjectMissing),
                // day0 machine 11: 1 bit, fully real
                Obj(11, Day(0) + 1, ComponentType, BitSolderJoint, BitSolderJoint),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };
        var filter = new DpmoTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            Opportunity: DpmoOpportunity.Components, SkipExclusion: SkipExclusion.Raw);

        var result = await DpmoTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(TimeBucket.Day, result.Bucket);
        Assert.Equal(DpmoOpportunity.Components, result.Opportunity);
        Assert.Equal(3, result.Buckets.Count);
        Assert.Equal("2026-01-01", result.Buckets[0].Label);

        // Ordered by machine name: AOI-10 then AOI-11.
        Assert.Equal(2, result.Lines.Count);
        var line10 = result.Lines[0];
        var line11 = result.Lines[1];
        Assert.Equal(10, line10.MachineId);
        Assert.Equal("AOI-10", line10.MachineName);

        // Machine 10 has a card in every bucket, so a point in every bucket —
        // including day2, where it inspected 100 and found nothing.
        Assert.Equal([0, 1, 2], line10.Points.Select(p => p.BucketIndex).ToArray());

        // day0: opportunities 100; AOI bits 3, Real 1, Dummy 2.
        var d0 = line10.Points[0].Kpi;
        Assert.Equal(100L, d0.OpportunityCount);
        Assert.Equal(3L, d0.DefectsAoi);
        Assert.Equal(1L, d0.DefectsReal);
        Assert.Equal(2L, d0.DefectsDummy);
        Assert.Equal(30_000d, d0.DpmoAoi);
        Assert.Equal(10_000d, d0.DpmoReal);
        Assert.Equal(20_000d, d0.DpmoDummy);

        // day2: inspected but clean -> a real zero, not a gap.
        var d2 = line10.Points[2].Kpi;
        Assert.Equal(100L, d2.OpportunityCount);
        Assert.Equal(0L, d2.DefectsAoi);
        Assert.Equal(0d, d2.DpmoAoi);

        // Line overall: 300 opportunities, 4 AOI bits, 2 real, 2 dummy.
        Assert.Equal(300L, line10.Overall.OpportunityCount);
        Assert.Equal(4L, line10.Overall.DefectsAoi);
        Assert.Equal(2L, line10.Overall.DefectsReal);
        Assert.Equal(2L, line10.Overall.DefectsDummy);
        Assert.Equal(1_000_000d * 4 / 300, line10.Overall.DpmoAoi);

        // Machine 11: points at buckets 0 and 2 only — bucket 1 is a gap
        // because no card was inspected, not because nothing was found.
        Assert.Equal([0, 2], line11.Points.Select(p => p.BucketIndex).ToArray());
        Assert.Equal(20_000d, line11.Points[0].Kpi.DpmoAoi);
        Assert.Equal(0d, line11.Points[1].Kpi.DpmoAoi);
    }

    [Fact]
    public async Task LineOverall_EqualsSumOfItsBuckets()
    {
        // Vieweb #12421 guard: aggregate counts, then divide once. The window
        // total must be derivable from the per-bucket counts.
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, Day(0), nbTestsOnComp: 300),
                Card(10, Day(1), nbTestsOnComp: 700),
            ],
            SeededTestedObjects =
            [
                Obj(10, Day(0) + 1, ComponentType, BitObjectMissing | BitSolderJoint, BitObjectMissing),
                Obj(10, Day(1) + 1, ComponentType, BitPolarityError, 0),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new DpmoTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            SkipExclusion: SkipExclusion.Raw);

        var result = await DpmoTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        var line = Assert.Single(result.Lines);
        Assert.Equal(line.Points.Sum(p => p.Kpi.OpportunityCount), line.Overall.OpportunityCount);
        Assert.Equal(line.Points.Sum(p => p.Kpi.DefectsAoi), line.Overall.DefectsAoi);
        Assert.Equal(line.Points.Sum(p => p.Kpi.DefectsReal), line.Overall.DefectsReal);
        Assert.Equal(line.Points.Sum(p => p.Kpi.DefectsDummy), line.Overall.DefectsDummy);

        // And the overall rate is NOT the mean of the bucket rates.
        Assert.Equal(1_000_000d * 3 / 1000, line.Overall.DpmoAoi);
        Assert.NotEqual(line.Points.Average(p => p.Kpi.DpmoAoi), line.Overall.DpmoAoi);
    }

    [Fact]
    public async Task Week_Bucketing_SplitsIsoWeeks()
    {
        // 2026-01-05 is a Monday. Two full ISO weeks -> two buckets.
        var window = new DateRange(
            new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 19, 0, 0, 0, TimeSpan.Zero));
        var wkStart = (int)window.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, wkStart + 3_600, nbTestsOnComp: 100),
                Card(10, wkStart + (7 * 86_400) + 3_600, nbTestsOnComp: 100),
            ],
            SeededTestedObjects =
            [
                Obj(10, wkStart + 3_601, ComponentType, BitObjectMissing, BitObjectMissing),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new DpmoTrendFilter(
            window, TimeBucket.Week, SiteTimeZone: TimeZoneInfo.Utc,
            SkipExclusion: SkipExclusion.Raw);

        var result = await DpmoTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Buckets.Count);
        var line = Assert.Single(result.Lines);
        Assert.Equal([0, 1], line.Points.Select(p => p.BucketIndex).ToArray());
        Assert.Equal(10_000d, line.Points[0].Kpi.DpmoAoi);
        Assert.Equal(0d, line.Points[1].Kpi.DpmoAoi);
    }

    [Fact]
    public async Task Components_Opportunity_IgnoresPastePadDefects()
    {
        // A paste-pad defect must not inflate a components numerator, whose
        // denominator is Nb_Of_Tests_On_Comp.
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, Day(0), nbTestsOnComp: 100, nbTestsOnPads: 400)],
            SeededTestedObjects =
            [
                Obj(10, Day(0) + 1, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(10, Day(0) + 2, PastePadType, BitSolderJoint, BitSolderJoint),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new DpmoTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            Opportunity: DpmoOpportunity.Components, SkipExclusion: SkipExclusion.Raw);

        var result = await DpmoTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        var kpi = Assert.Single(result.Lines).Points[0].Kpi;
        Assert.Equal(100L, kpi.OpportunityCount); // comp only, pads excluded
        Assert.Equal(1L, kpi.DefectsAoi);         // paste defect excluded
        Assert.Equal(10_000d, kpi.DpmoAoi);
    }

    [Fact]
    public async Task All_Opportunity_AddsPasteOnlyWhenSourceRecordsIt()
    {
        // Same seeded cards on both sources. The pre-reflow source advertises
        // PastePrintMetrics so _On_Pads joins the denominator; the post-reflow
        // one does not, so an "All" trend there is components-only.
        static FakeAoiSource Build(SourceDescriptor descriptor) => new(descriptor)
        {
            SeededCards = [Card(10, Day(0), nbTestsOnComp: 100, nbTestsOnPads: 400)],
            SeededTestedObjects =
            [
                Obj(10, Day(0) + 1, ComponentType, BitObjectMissing, BitObjectMissing),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new DpmoTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            Opportunity: DpmoOpportunity.All, SkipExclusion: SkipExclusion.Raw);

        var pre = await DpmoTrendByLineReport.Instance.RunAsync(
            Build(_preReflow), filter, TestContext.Current.CancellationToken);
        var post = await DpmoTrendByLineReport.Instance.RunAsync(
            Build(_postReflow), filter, TestContext.Current.CancellationToken);

        Assert.Equal(500L, Assert.Single(pre.Lines).Overall.OpportunityCount);  // 100 + 400
        Assert.Equal(100L, Assert.Single(post.Lines).Overall.OpportunityCount); // comp only
    }

    [Fact]
    public async Task MachineIdsFilter_NarrowsToTheRequestedLine()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, Day(0), nbTestsOnComp: 100),
                Card(11, Day(0), nbTestsOnComp: 100),
            ],
            SeededTestedObjects =
            [
                Obj(10, Day(0) + 1, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(11, Day(0) + 1, ComponentType, BitSolderJoint, BitSolderJoint),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };
        var filter = new DpmoTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            MachineIds: [10], SkipExclusion: SkipExclusion.Raw);

        var result = await DpmoTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        var line = Assert.Single(result.Lines);
        Assert.Equal(10, line.MachineId);
        Assert.Equal(100L, line.Overall.OpportunityCount);
    }

    [Fact]
    public async Task WindowTotal_MatchesDpmoTableReport_ForTheSameScope()
    {
        // Parity guard: the trend is a re-keying of the DPMO table, so summing
        // the trend across every line and bucket must reproduce the table's
        // overall for the same window, opportunity flavour, and skip mode.
        static FakeAoiSource Build(SourceDescriptor descriptor) => new(descriptor)
        {
            SeededCards =
            [
                Card(10, (int)_oneDay.StartEpochSeconds + 10, nbTestsOnComp: 100),
                Card(11, (int)_oneDay.StartEpochSeconds + 20, nbTestsOnComp: 50),
            ],
            SeededTestedObjects =
            [
                Obj(10, (int)_oneDay.StartEpochSeconds + 60, ComponentType,
                    BitObjectMissing | BitPolarityError, BitObjectMissing),
                Obj(10, (int)_oneDay.StartEpochSeconds + 62, ComponentType, BitSolderJoint, 0),
                Obj(11, (int)_oneDay.StartEpochSeconds + 70, ComponentType,
                    BitObjectMissing, BitObjectMissing),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        var tableFilter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Real, DpmoOpportunity.Components,
            SkipExclusion: SkipExclusion.Raw);
        var table = await DpmoTableReport.Instance.RunAsync(
            Build(_postReflow), tableFilter, TestContext.Current.CancellationToken);

        var trendFilter = new DpmoTrendFilter(
            _oneDay, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            Opportunity: DpmoOpportunity.Components, SkipExclusion: SkipExclusion.Raw);
        var trend = await DpmoTrendByLineReport.Instance.RunAsync(
            Build(_postReflow), trendFilter, TestContext.Current.CancellationToken);

        var trendOpportunities = trend.Lines.Sum(l => l.Overall.OpportunityCount);
        var trendRealDefects = trend.Lines.Sum(l => l.Overall.DefectsReal);

        Assert.Equal(table.Overall.OpportunityCount, trendOpportunities);
        Assert.Equal(table.Overall.DefectBitCount, trendRealDefects);
        Assert.Equal(
            table.Overall.DpmoPpm,
            1_000_000d * trendRealDefects / trendOpportunities);
    }

    private static TestedObjectRow Obj(
        int machineId,
        int date,
        int objectTypeId,
        long errorTable,
        long errorTableAr,
        int productId = 500)
    {
        return new TestedObjectRow(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: date, // unique per row
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            Topology: null,
            PartNumberName: null,
            JedecName: null);
    }

    // Builds a CARDS row carrying the DPMO opportunity denominator. The
    // denominator MUST come from cards like this — never from a
    // TESTED_OBJECT row count (see DpmoTrendByLineReport remarks).
    private static CardRow Card(
        int machineId,
        int date,
        int nbTestsOnComp,
        int productId = 500,
        int? nbTestsOnPads = null)
    {
        return new CardRow(
            PanelId: 1,
            CardIdOnPanel: 1,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            NbOfTestsOnComp: nbTestsOnComp,
            NbOfTestsOnPads: nbTestsOnPads);
    }
}
