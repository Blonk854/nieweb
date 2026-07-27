using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for the <c>SkipStatuses</c> positive narrowing filter on
/// <see cref="DpmoTableReport"/> and <see cref="FpyTableReport"/>:
/// when set, only boards whose computed <see cref="SkipClass"/> is in
/// the set contribute. Composes with <see cref="SkipExclusion"/>.
/// </summary>
public sealed class SkipStatusFilterTests
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

    private const int ComponentType = 0x01;
    private const long ObjectMissing = 1L;

    // ---- DPMO -------------------------------------------------------------

    /// <summary>
    /// A real board (None) + an X-OUT'd empty board (ManualSkip).
    /// Filtering to ManualSkip keeps ONLY the empty board — the inverse
    /// of Clean — so the report isolates the skipped population.
    /// </summary>
    [Fact]
    public async Task Dpmo_SkipStatuses_ManualSkip_KeepsOnlyTheSkippedBoard()
    {
        var source = TwoBoardSource();
        var baseFilter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.Components);

        var manualOnly = await DpmoTableReport.Instance.RunAsync(
            source,
            baseFilter with { SkipStatuses = [SkipClass.ManualSkip] },
            TestContext.Current.CancellationToken);

        // Only card 2 (the X-OUT empty board) survives: 100 tests, 50 phantom
        // missings → 500 000 DPMO. Card 1 (real, None) is filtered out.
        Assert.Equal(100L, manualOnly.Overall.OpportunityCount);
        Assert.Equal(50L, manualOnly.Overall.DefectBitCount);
        Assert.Equal(500_000d, manualOnly.Overall.DpmoPpm);
        Assert.Equal(1L, manualOnly.SkipExcludedCards);
    }

    /// <summary>
    /// Filtering to None is equivalent to Clean: only the real board
    /// survives.
    /// </summary>
    [Fact]
    public async Task Dpmo_SkipStatuses_None_MatchesClean()
    {
        var source = TwoBoardSource();
        var baseFilter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.Components);

        var noneOnly = await DpmoTableReport.Instance.RunAsync(
            source,
            baseFilter with { SkipStatuses = [SkipClass.None] },
            TestContext.Current.CancellationToken);
        var clean = await DpmoTableReport.Instance.RunAsync(
            source,
            baseFilter with { SkipExclusion = SkipExclusion.Clean },
            TestContext.Current.CancellationToken);

        Assert.Equal(clean.Overall.OpportunityCount, noneOnly.Overall.OpportunityCount);
        Assert.Equal(clean.Overall.DefectBitCount, noneOnly.Overall.DefectBitCount);
        Assert.Equal(clean.Overall.DpmoPpm, noneOnly.Overall.DpmoPpm);
        Assert.Equal(100L, noneOnly.Overall.OpportunityCount);
        Assert.Equal(1L, noneOnly.Overall.DefectBitCount);
    }

    // ---- FPY --------------------------------------------------------------

    /// <summary>
    /// Board-level FPY narrowed to ManualSkip keeps only the X-OUT board
    /// (faulty) → FPY 0 %; the two good boards are filtered out.
    /// </summary>
    [Fact]
    public async Task Fpy_Board_SkipStatuses_ManualSkip_KeepsOnlySkippedBoard()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, panelStatus: 1)],
            SeededCards =
            [
                Card(1, 1, cardStatus: 1),   // None (good)
                Card(1, 2, cardStatus: -2),  // ManualSkip (X-OUT) — faulty
                Card(1, 3, cardStatus: 1),   // None (good)
            ],
            SeededTestedObjects = [To(1, 2, repairButton: "X-OUT", objId: 1)],
        };

        var result = await FpyTableReport.Instance.RunAsync(
            source,
            new FpyTableFilter(
                _oneDay, FpyGranularity.Board, FpyGroupBy.AoiMachine,
                SkipStatuses: [SkipClass.ManualSkip]),
            TestContext.Current.CancellationToken);

        // Only the faulty X-OUT board remains → 1 row, FPY 0 %.
        Assert.Equal(1L, result.Overall.TotalRows);
        Assert.Equal(0L, result.Overall.GoodAoiCount);
        Assert.Equal(0d, result.Overall.FpyAoiPercent);
        Assert.Equal(2L, result.SkipExcludedRows); // the two good boards
    }

    /// <summary>
    /// Board-level FPY narrowed to None equals Clean: only the good
    /// boards survive.
    /// </summary>
    [Fact]
    public async Task Fpy_Board_SkipStatuses_None_MatchesClean()
    {
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, panelStatus: 1)],
            SeededCards =
            [
                Card(1, 1, cardStatus: 1),
                Card(1, 2, cardStatus: -2),
                Card(1, 3, cardStatus: 1),
            ],
            SeededTestedObjects = [To(1, 2, repairButton: "X-OUT", objId: 1)],
        };

        var noneOnly = await FpyTableReport.Instance.RunAsync(
            source,
            new FpyTableFilter(
                _oneDay, FpyGranularity.Board, FpyGroupBy.AoiMachine,
                SkipStatuses: [SkipClass.None]),
            TestContext.Current.CancellationToken);
        var clean = await FpyTableReport.Instance.RunAsync(
            source,
            new FpyTableFilter(
                _oneDay, FpyGranularity.Board, FpyGroupBy.AoiMachine,
                SkipExclusion: SkipExclusion.Clean),
            TestContext.Current.CancellationToken);

        Assert.Equal(clean.Overall.TotalRows, noneOnly.Overall.TotalRows);
        Assert.Equal(clean.Overall.FpyAoiPercent, noneOnly.Overall.FpyAoiPercent);
        Assert.Equal(2L, noneOnly.Overall.TotalRows);
        Assert.Equal(100d, noneOnly.Overall.FpyAoiPercent);
    }

    // ---- builders ---------------------------------------------------------

    private static FakeAoiSource TwoBoardSource()
    {
        // Card 1: real board — 100 comp tests, 1 genuine defect (None).
        // Card 2: X-OUT'd empty board — 100 comp tests, 50 phantom missings
        //         (the first carries the X-OUT) → ManualSkip.
        var tos = new List<TestedObjectRow> { Obj(1, 1, ObjectMissing, objId: 1) };
        for (var i = 0; i < 50; i++)
        {
            tos.Add(Obj(1, 2, ObjectMissing, objId: 100 + i, repairButton: i == 0 ? "X-OUT" : null));
        }

        return new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, panelStatus: 1)],
            SeededCards =
            [
                DpmoCard(1, 1, nbTestsOnComp: 100),
                DpmoCard(1, 2, nbTestsOnComp: 100),
            ],
            SeededTestedObjects = tos,
        };
    }

    private static PanelRow Panel(int id, int panelStatus, bool reviewed = true) => new(
        PanelId: id,
        MachineId: 10,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D3}",
        PanelNumericDate: Start + id,
        NbOfValidCards: 3,
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

    private static CardRow Card(int panelId, int cardId, int cardStatus) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: cardStatus,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: 100,
        NbOfErrorObject: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: Start + panelId);

    private static CardRow DpmoCard(int panelId, int cardId, int nbTestsOnComp) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: 1,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: nbTestsOnComp,
        NbOfErrorObject: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: Start + panelId,
        NbOfTestsOnComp: nbTestsOnComp);

    private static TestedObjectRow Obj(int panel, int card, long errorTable, int objId, string? repairButton = null) => new(
        PanelId: panel,
        CardIdOnPanel: card,
        ObjectId: objId,
        ObjectTypeId: ComponentType,
        ErrorTable: errorTable,
        ErrorTableAr: errorTable,
        Status: errorTable == 0 ? 0 : 1,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: Start + panel,
        Topology: null,
        PartNumberName: null,
        JedecName: null,
        RepairButtonComment: repairButton);

    private static TestedObjectRow To(int panel, int card, long errorTable = 0, string? repairButton = null, int objId = 0) => new(
        PanelId: panel,
        CardIdOnPanel: card,
        ObjectId: objId,
        ObjectTypeId: ComponentType,
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
