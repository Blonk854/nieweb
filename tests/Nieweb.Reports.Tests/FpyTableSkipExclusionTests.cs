using Nieweb.DataSources;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="SkipExclusion.Clean"/> on
/// <see cref="FpyTableReport"/>: board-level exclusion, panel-level
/// re-derivation from surviving boards, fully-skipped-panel exclusion,
/// and the invariant that clean-with-no-skips equals raw.
/// </summary>
public sealed class FpyTableSkipExclusionTests
{
    private static readonly SourceDescriptor _postReflow = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI",
        SchemaVersion: "5.0",
        Caps: Capabilities.PinLevel | Capabilities.IsLastInspectionFilter);

    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    private static int Start => (int)_oneDay.StartEpochSeconds;

    private static Task<FpyTableResult> RunAsync(FakeAoiSource source, FpyGranularity granularity, SkipExclusion skip)
        => FpyTableReport.Instance.RunAsync(
            source,
            new FpyTableFilter(_oneDay, granularity, FpyGroupBy.AoiMachine, SkipExclusion: skip),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Board_Clean_ExcludesSkippedBoards()
    {
        // Panel 1 (reviewed): good board, X-OUT board (faulty), good board.
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, panelStatus: 1)],
            SeededCards =
            [
                Card(1, 1, cardStatus: 1),   // None (good)
                Card(1, 2, cardStatus: -2),  // ManualSkip (X-OUT) — faulty board
                Card(1, 3, cardStatus: 1),   // None (good)
            ],
            SeededTestedObjects = [To(1, 2, repairButton: "X-OUT", objId: 1)],
        };

        var raw = await RunAsync(source, FpyGranularity.Board, SkipExclusion.Raw);
        var clean = await RunAsync(source, FpyGranularity.Board, SkipExclusion.Clean);

        // Raw counts the faulty X-OUT board → FPY 2/3.
        Assert.Equal(3L, raw.Overall.TotalRows);
        Assert.Equal(100d * 2 / 3, raw.Overall.FpyAoiPercent);
        Assert.Equal(0L, raw.SkipExcludedRows);

        // Clean drops it → FPY 2/2 = 100%.
        Assert.Equal(2L, clean.Overall.TotalRows);
        Assert.Equal(2L, clean.Overall.GoodAoiCount);
        Assert.Equal(100d, clean.Overall.FpyAoiPercent);
        Assert.Equal(1L, clean.SkipExcludedRows);
        Assert.Equal(SkipExclusion.Clean, clean.SkipExclusion);
    }

    [Fact]
    public async Task Panel_Clean_ReDerivesGoodnessFromSurvivingBoards()
    {
        // The panel is faulty (Panel_Status -2) ONLY because one board is
        // an X-OUT'd empty board. Its real board is good, so the clean
        // panel should be re-derived as good.
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, panelStatus: -2)],
            SeededCards =
            [
                Card(1, 1, cardStatus: 1),   // None (real, good)
                Card(1, 2, cardStatus: -2),  // ManualSkip (X-OUT empty board)
            ],
            SeededTestedObjects = [To(1, 2, repairButton: "X-OUT", objId: 1)],
        };

        var raw = await RunAsync(source, FpyGranularity.Panel, SkipExclusion.Raw);
        var clean = await RunAsync(source, FpyGranularity.Panel, SkipExclusion.Clean);

        // Raw: the panel is faulty → FPY 0%.
        Assert.Equal(1L, raw.Overall.TotalRows);
        Assert.Equal(0d, raw.Overall.FpyAoiPercent);

        // Clean: re-derived from the surviving good board → FPY 100%.
        Assert.Equal(1L, clean.Overall.TotalRows);
        Assert.Equal(1L, clean.Overall.GoodAoiCount);
        Assert.Equal(1L, clean.Overall.InspectedCount);
        Assert.Equal(100d, clean.Overall.FpyAoiPercent);
        Assert.Equal(0L, clean.SkipExcludedRows); // re-derived, not excluded
    }

    [Fact]
    public async Task Panel_Clean_ExcludesFullySkippedPanel()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(1, panelStatus: -2), // fully skipped
                Panel(2, panelStatus: 1),  // clean good
            ],
            SeededCards =
            [
                Card(1, 1, cardStatus: -2),  // ManualSkip — the panel's only board
                Card(2, 1, cardStatus: 1),   // None (good)
            ],
            SeededTestedObjects = [To(1, 1, repairButton: "X-OUT", objId: 1)],
        };

        var raw = await RunAsync(source, FpyGranularity.Panel, SkipExclusion.Raw);
        var clean = await RunAsync(source, FpyGranularity.Panel, SkipExclusion.Clean);

        Assert.Equal(2L, raw.Overall.TotalRows);
        Assert.Equal(50d, raw.Overall.FpyAoiPercent);

        // Panel 1 wholly skipped → excluded; panel 2 good → FPY 100%.
        Assert.Equal(1L, clean.Overall.TotalRows);
        Assert.Equal(1L, clean.Overall.GoodAoiCount);
        Assert.Equal(100d, clean.Overall.FpyAoiPercent);
        Assert.Equal(1L, clean.SkipExcludedRows);
    }

    [Fact]
    public async Task Panel_Clean_WithNoSkips_EqualsRaw()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(1, panelStatus: 1),
                Panel(2, panelStatus: -2),
            ],
            SeededCards =
            [
                Card(1, 1, cardStatus: 1),
                Card(2, 1, cardStatus: -2),
            ],
            // No X-OUT, no machine skip, no missing → no skips.
        };

        var raw = await RunAsync(source, FpyGranularity.Panel, SkipExclusion.Raw);
        var clean = await RunAsync(source, FpyGranularity.Panel, SkipExclusion.Clean);

        Assert.Equal(raw.Overall.TotalRows, clean.Overall.TotalRows);
        Assert.Equal(raw.Overall.FpyAoiPercent, clean.Overall.FpyAoiPercent);
        Assert.Equal(50d, clean.Overall.FpyAoiPercent);
        Assert.Equal(0L, clean.SkipExcludedRows);
    }

    // ---- builders ---------------------------------------------------------

    private static PanelRow Panel(int id, int panelStatus, bool reviewed = true) => new(
        PanelId: id,
        MachineId: 10,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D3}",
        PanelNumericDate: Start + id,
        NbOfValidCards: 1,
        TestTime: 5.0,
        PanelStatus: panelStatus,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: reviewed,
        NbOfTestedObject: 100,
        NbOfErrorObject: 0,
        OperatorId: null,
        ProductId: 500,
        RecipeId: 1);

    private static CardRow Card(int panelId, int cardId, int cardStatus, int components = 100, long anomalyAr = 0) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: cardStatus,
        AnomalyBr: 0,
        AnomalyAr: anomalyAr,
        NbOfTestedObject: components,
        NbOfErrorObject: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: Start + panelId);

    private static TestedObjectRow To(int panel, int card, long errorTable = 0, string? repairButton = null, int objId = 0) => new(
        PanelId: panel,
        CardIdOnPanel: card,
        ObjectId: objId,
        ObjectTypeId: 0x01,
        ErrorTable: errorTable,
        ErrorTableAr: 0,
        Status: errorTable == 0 ? 0 : 1,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: Start + panel,
        Topology: null,
        PartNumberName: null,
        JedecName: null,
        RepairButtonComment: repairButton);
}
