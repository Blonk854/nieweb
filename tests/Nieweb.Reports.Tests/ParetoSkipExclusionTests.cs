using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Skip filtering on <see cref="ParetoReport"/> — mirrors the DPMO
/// behaviour: <see cref="SkipExclusion.Clean"/> drops defects on skipped
/// boards, and <see cref="ParetoFilter.SkipStatuses"/> narrows to
/// specific <see cref="SkipClass"/> values.
/// </summary>
public sealed class ParetoSkipExclusionTests
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

    private static FakeAoiSource BuildSource()
    {
        // Card 1: a real board — 1 genuine defect.
        // Card 2: an operator X-OUT'd empty board — 50 phantom missings
        //         (the first carries the X-OUT review button).
        var tos = new List<TestedObjectRow> { Obj(1, 1, ObjectMissing, objId: 1) };
        for (var i = 0; i < 50; i++)
        {
            tos.Add(Obj(1, 2, ObjectMissing, objId: 100 + i, repairButton: i == 0 ? "X-OUT" : null));
        }

        return new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, reviewed: true)],
            SeededCards = [Card(1, 1, nbTestsOnComp: 100), Card(1, 2, nbTestsOnComp: 100)],
            SeededTestedObjects = tos,
        };
    }

    [Fact]
    public async Task Clean_ExcludesSkippedBoardDefects()
    {
        var source = BuildSource();
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Defect, Numerator: DpmoNumerator.Aoi);

        var raw = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);
        var clean = await ParetoReport.Instance.RunAsync(
            source, filter with { SkipExclusion = SkipExclusion.Clean },
            TestContext.Current.CancellationToken);

        // Raw: every board counts → 51 "Object missing" defects.
        Assert.Equal(51L, raw.Overall.DefectBitCount);
        Assert.Equal(0L, raw.SkipExcludedCards);
        Assert.Equal(SkipExclusion.Raw, raw.SkipExclusion);

        // Clean: the X-OUT'd board drops out → 1 real defect.
        Assert.Equal(1L, clean.Overall.DefectBitCount);
        Assert.Equal(1L, clean.SkipExcludedCards);
        Assert.Equal(SkipExclusion.Clean, clean.SkipExclusion);
        Assert.Single(clean.Rows);
        Assert.Equal("Object missing", clean.Rows[0].GroupName);
        Assert.Equal(1L, clean.Rows[0].DefectCount);
    }

    [Fact]
    public async Task SkipStatuses_NarrowsToManualSkipBoardsOnly()
    {
        var source = BuildSource();
        // Keep ONLY manual-skip boards: card 2 (X-OUT) stays, card 1 drops.
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Defect, Numerator: DpmoNumerator.Aoi)
        {
            SkipStatuses = [SkipClass.ManualSkip],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // Only the 50 phantom missings on the X-OUT board remain.
        Assert.Equal(50L, result.Overall.DefectBitCount);
        Assert.Equal(1L, result.SkipExcludedCards); // card 1 (None) excluded
        Assert.Single(result.Rows);
        Assert.Equal(50L, result.Rows[0].DefectCount);
    }

    // ---- builders (match DpmoTableSkipExclusionTests) ---------------------

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
}
