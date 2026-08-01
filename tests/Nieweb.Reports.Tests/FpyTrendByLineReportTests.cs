using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="FpyTrendByLineReport"/>: day / week bucketing,
/// per-line series with gap semantics, the three FPY flavours per point,
/// panel vs board (sub-panel) granularity, and Clean skip exclusion
/// (which shares the FpyTableReport machinery).
/// </summary>
public sealed class FpyTrendByLineReportTests
{
    private static readonly SourceDescriptor _postReflow = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI",
        SchemaVersion: "5.0",
        Caps: Capabilities.PinLevel | Capabilities.IsLastInspectionFilter);

    // Three full UTC days: 2026-01-01 .. 2026-01-04.
    private static readonly DateRange _threeDays = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero));

    private static int Start => (int)_threeDays.StartEpochSeconds;

    /// <summary>Epoch seconds one hour into day <paramref name="dayIndex"/> of the window.</summary>
    private static int Day(int dayIndex) => Start + (dayIndex * 86_400) + 3_600;

    [Fact]
    public async Task Rejects_NonDayWeek_Bucket()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new FpyTrendFilter(_threeDays, TimeBucket.Hour6, SiteTimeZone: TimeZoneInfo.Utc);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => FpyTrendByLineReport.Instance.RunAsync(
                source, filter, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Panel_Day_PerLineSeries_Flavours_Gaps_Ordering()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                // Machine 10: day0 {1,1,-1}, day1 {1}, day2 {2}
                Panel(1, 10, Day(0), 1),
                Panel(2, 10, Day(0), 1),
                Panel(3, 10, Day(0), -1),
                Panel(4, 10, Day(1), 1),
                Panel(5, 10, Day(2), 2),
                // Machine 11: day0 {1}, day2 {-2}  (no day1 => gap)
                Panel(6, 11, Day(0), 1),
                Panel(7, 11, Day(2), -2),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };
        var filter = new FpyTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            Granularity: FpyGranularity.Panel, SkipExclusion: SkipExclusion.Raw);

        var result = await FpyTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(TimeBucket.Day, result.Bucket);
        Assert.Equal(FpyGranularity.Panel, result.Granularity);
        Assert.Equal(3, result.Buckets.Count);
        Assert.Equal("2026-01-01", result.Buckets[0].Label);

        // Ordered by machine name: AOI-10 then AOI-11.
        Assert.Equal(2, result.Lines.Count);
        var line10 = result.Lines[0];
        var line11 = result.Lines[1];
        Assert.Equal(10, line10.MachineId);
        Assert.Equal("AOI-10", line10.MachineName);
        Assert.Equal(11, line11.MachineId);

        // Machine 10 has a point in every bucket.
        Assert.Equal([0, 1, 2], line10.Points.Select(p => p.BucketIndex).ToArray());
        // day0 {1,1,-1}: AOI 2/3, Diag 2/3.
        Assert.Equal(100d * 2 / 3, line10.Points[0].Kpi.FpyAoiPercent);
        Assert.Equal(100d * 2 / 3, line10.Points[0].Kpi.FpyDiagnosticPercent);
        // day2 {2}: AOI 0, Diag 100 (the flavour toggle payoff).
        Assert.Equal(0d, line10.Points[2].Kpi.FpyAoiPercent);
        Assert.Equal(100d, line10.Points[2].Kpi.FpyDiagnosticPercent);
        // Line overall: inspected 5, GoodAoi 3, GoodDiag 4.
        Assert.Equal(5, line10.Overall.InspectedCount);
        Assert.Equal(60d, line10.Overall.FpyAoiPercent);
        Assert.Equal(80d, line10.Overall.FpyDiagnosticPercent);

        // Machine 11: point at bucket 0 and 2 only — bucket 1 is a gap.
        Assert.Equal([0, 2], line11.Points.Select(p => p.BucketIndex).ToArray());
        Assert.Equal(100d, line11.Points[0].Kpi.FpyAoiPercent);
        Assert.Equal(0d, line11.Points[1].Kpi.FpyAoiPercent);
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
            SeededPanels =
            [
                Panel(1, 10, wkStart + 3_600, 1),                    // week 0
                Panel(2, 10, wkStart + (7 * 86_400) + 3_600, -1),   // week 1
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new FpyTrendFilter(
            window, TimeBucket.Week, SiteTimeZone: TimeZoneInfo.Utc,
            Granularity: FpyGranularity.Panel, SkipExclusion: SkipExclusion.Raw);

        var result = await FpyTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Buckets.Count);
        var line = Assert.Single(result.Lines);
        Assert.Equal([0, 1], line.Points.Select(p => p.BucketIndex).ToArray());
        Assert.Equal(100d, line.Points[0].Kpi.FpyAoiPercent); // week 0: the good panel
        Assert.Equal(0d, line.Points[1].Kpi.FpyAoiPercent);   // week 1: the faulty panel
    }

    [Fact]
    public async Task Board_Raw_PerBucketBoardFpy()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(1, 1, 10, Day(0), 1),
                Card(1, 2, 10, Day(0), -1),
                Card(2, 1, 10, Day(1), 1),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new FpyTrendFilter(
            _threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
            Granularity: FpyGranularity.Board, SkipExclusion: SkipExclusion.Raw);

        var result = await FpyTrendByLineReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        var line = Assert.Single(result.Lines);
        Assert.Equal([0, 1], line.Points.Select(p => p.BucketIndex).ToArray());
        Assert.Equal(50d, line.Points[0].Kpi.FpyAoiPercent);  // day0: 1 good / 2 boards
        Assert.Equal(100d, line.Points[1].Kpi.FpyAoiPercent); // day1: 1 good / 1 board
    }

    [Fact]
    public async Task Board_Clean_ExcludesSkippedBoard()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, 10, Day(0), 1, reviewed: true)],
            SeededCards =
            [
                Card(1, 1, 10, Day(0), 1),   // good
                Card(1, 2, 10, Day(0), -2),  // X-OUT skipped board
            ],
            SeededTestedObjects = [To(1, 2, Day(0), repairButton: "X-OUT", objId: 1)],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };

        var raw = await FpyTrendByLineReport.Instance.RunAsync(
            source,
            new FpyTrendFilter(_threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
                Granularity: FpyGranularity.Board, SkipExclusion: SkipExclusion.Raw),
            TestContext.Current.CancellationToken);
        var clean = await FpyTrendByLineReport.Instance.RunAsync(
            source,
            new FpyTrendFilter(_threeDays, TimeBucket.Day, SiteTimeZone: TimeZoneInfo.Utc,
                Granularity: FpyGranularity.Board, SkipExclusion: SkipExclusion.Clean),
            TestContext.Current.CancellationToken);

        // Raw counts the faulty X-OUT board -> day0 FPY 1/2 = 50%.
        Assert.Equal(50d, Assert.Single(raw.Lines).Points[0].Kpi.FpyAoiPercent);

        // Clean drops it -> day0 FPY 1/1 = 100%, one excluded row.
        var cleanLine = Assert.Single(clean.Lines);
        Assert.Equal(100d, cleanLine.Points[0].Kpi.FpyAoiPercent);
        Assert.Equal(1L, clean.SkipExcludedRows);
    }

    // ---- builders ---------------------------------------------------------

    private static PanelRow Panel(int id, int machineId, int date, int status, bool reviewed = false, int productId = 500) => new(
        PanelId: id,
        MachineId: machineId,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D6}",
        PanelNumericDate: date,
        NbOfValidCards: 4,
        TestTime: 12.5,
        PanelStatus: status,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: reviewed,
        NbOfTestedObject: 100,
        NbOfErrorObject: status is (-2) or (-1) ? 3 : 0,
        OperatorId: 42,
        ProductId: productId,
        RecipeId: 600);

    private static CardRow Card(int panelId, int cardId, int machineId, int date, int status, int productId = 500) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: status,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: 25,
        NbOfErrorObject: status is (-2) or (-1) ? 2 : 0,
        MachineId: machineId,
        ProductId: productId,
        PanelNumericDate: date);

    private static TestedObjectRow To(int panel, int card, int date, string? repairButton = null, int objId = 0) => new(
        PanelId: panel,
        CardIdOnPanel: card,
        ObjectId: objId,
        ObjectTypeId: 0x01,
        ErrorTable: 0,
        ErrorTableAr: 0,
        Status: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: date,
        Topology: null,
        PartNumberName: null,
        JedecName: null,
        RepairButtonComment: repairButton);
}
