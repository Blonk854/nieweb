using Nieweb.DataSources;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="SkipExclusion.Clean"/> on
/// <see cref="DpmoTableReport"/>: a skipped / empty board's phantom
/// "missing" defects are removed from BOTH the numerator and the
/// opportunity denominator, so DPMO reflects the real population.
/// </summary>
public sealed class DpmoTableSkipExclusionTests
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

    [Fact]
    public async Task Clean_ExcludesSkippedBoardFromNumeratorAndDenominator()
    {
        // Card 1: a real board — 100 comp tests, 1 genuine defect.
        // Card 2: an X-OUT'd empty board — 100 comp tests, 50 phantom
        //         "missing" defects (one of them carries the X-OUT).
        var tos = new List<TestedObjectRow>
        {
            Obj(1, 1, ObjectMissing, objId: 1), // card 1's single real defect
        };
        for (var i = 0; i < 50; i++)
        {
            // card 2's phantom missings; the first also carries the X-OUT.
            tos.Add(Obj(1, 2, ObjectMissing, objId: 100 + i, repairButton: i == 0 ? "X-OUT" : null));
        }

        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, reviewed: true)],
            SeededCards =
            [
                Card(1, 1, nbTestsOnComp: 100),
                Card(1, 2, nbTestsOnComp: 100),
            ],
            SeededTestedObjects = tos,
        };

        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.Components);

        var raw = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);
        var clean = await DpmoTableReport.Instance.RunAsync(
            source, filter with { SkipExclusion = SkipExclusion.Clean },
            TestContext.Current.CancellationToken);

        // Raw: both boards count → 51 defects / 200 tests → 255 000 DPMO.
        Assert.Equal(200L, raw.Overall.OpportunityCount);
        Assert.Equal(51L, raw.Overall.DefectBitCount);
        Assert.Equal(255_000d, raw.Overall.DpmoPpm);
        Assert.Equal(0L, raw.SkipExcludedCards);

        // Clean: the empty board drops out → 1 defect / 100 tests → 10 000 DPMO.
        Assert.Equal(100L, clean.Overall.OpportunityCount);
        Assert.Equal(1L, clean.Overall.DefectBitCount);
        Assert.Equal(10_000d, clean.Overall.DpmoPpm);
        Assert.Equal(1L, clean.SkipExcludedCards);
        Assert.Equal(SkipExclusion.Clean, clean.SkipExclusion);
    }

    // ---- builders ---------------------------------------------------------

    private static PanelRow Panel(int id, bool reviewed) => new(
        PanelId: id,
        MachineId: 10,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D3}",
        PanelNumericDate: Start + id,
        NbOfValidCards: 2,
        TestTime: 5.0,
        PanelStatus: 1,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: reviewed,
        NbOfTestedObject: 100,
        NbOfErrorObject: 0,
        OperatorId: null,
        ProductId: 500,
        RecipeId: 1);

    private static CardRow Card(int panelId, int cardId, int nbTestsOnComp) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: 1,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: nbTestsOnComp, // Number_Of_Component (also drives heuristic denom)
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
}
