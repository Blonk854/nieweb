using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="SkipSummaryReport"/>: a mixed panel exercising
/// every <see cref="SkipClass"/> plus the review gate, driven through
/// the full three-pass panel / tested-object / card join.
/// </summary>
public sealed class SkipSummaryReportTests
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

    private const long ObjectMissing = 1L;

    [Fact]
    public async Task Empty_ReturnsZeroTotalsAndFourClassRows()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new SkipSummaryFilter(_oneDay);

        var result = await SkipSummaryReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(0L, result.TotalCards);
        Assert.Equal(0L, result.SkippedCards);
        Assert.Equal(0d, result.SkippedCardPercent);
        Assert.Equal(4, result.Classes.Count); // one per SkipClass member
        Assert.All(result.Classes, c => Assert.Equal(0L, c.CardCount));
    }

    [Fact]
    public async Task MixedPanels_ClassifiesEveryMechanismAndHonoursReviewGate()
    {
        // Panel 1 (reviewed): normal card, X-OUT card, 60/100-missing card.
        // Panel 2 (reviewed): machine-skip card, tiny false-positive card.
        // Panel 3 (NOT reviewed): X-OUT card -> must NOT be ManualSkip.
        var tos = new List<TestedObjectRow>
        {
            To(panel: 1, card: 2, repairButton: "X-OUT", objId: 1),
            To(panel: 3, card: 1, repairButton: "X-OUT", objId: 2),
        };
        for (var i = 0; i < 60; i++)
        {
            tos.Add(To(panel: 1, card: 3, errorTable: ObjectMissing, objId: 100 + i));
        }
        for (var i = 0; i < 2; i++)
        {
            tos.Add(To(panel: 2, card: 2, errorTable: ObjectMissing, objId: 200 + i));
        }

        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(1, reviewed: true),
                Panel(2, reviewed: true),
                Panel(3, reviewed: false),
            ],
            SeededCards =
            [
                Card(1, 1, components: 100),                                  // None
                Card(1, 2, components: 100),                                  // ManualSkip (X-OUT)
                Card(1, 3, components: 100),                                  // HeuristicMissing (60/100)
                Card(2, 1, components: 100, anomalyAr: SkipClassifier.MachineSkipBit, cardStatus: 0), // MachineFlagged
                Card(2, 2, components: 4),                                    // None (tiny, false positive)
                Card(3, 1, components: 100),                                  // None (X-OUT but unreviewed)
            ],
            SeededTestedObjects = tos,
        };

        var result = await SkipSummaryReport.Instance.RunAsync(
            source, new SkipSummaryFilter(_oneDay), TestContext.Current.CancellationToken);

        Assert.Equal(6L, result.TotalCards);
        Assert.Equal(3L, result.SkippedCards);
        Assert.Equal(50d, result.SkippedCardPercent);
        Assert.Equal(504L, result.TotalComponents); // 100+100+100+100+4+100

        Assert.Equal(3L, ClassCount(result, SkipClass.None));
        Assert.Equal(1L, ClassCount(result, SkipClass.ManualSkip));
        Assert.Equal(1L, ClassCount(result, SkipClass.MachineFlagged));
        Assert.Equal(1L, ClassCount(result, SkipClass.HeuristicMissing));

        // Component volume attributed to each skip class.
        Assert.Equal(100L, ClassComponents(result, SkipClass.ManualSkip));
        Assert.Equal(100L, ClassComponents(result, SkipClass.HeuristicMissing));
    }

    private static long ClassCount(SkipSummaryResult r, SkipClass cls)
        => r.Classes.Single(c => c.Class == cls).CardCount;

    private static long ClassComponents(SkipSummaryResult r, SkipClass cls)
        => r.Classes.Single(c => c.Class == cls).ComponentCount;

    private static PanelRow Panel(int id, bool reviewed) => new(
        PanelId: id,
        MachineId: 10,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D3}",
        PanelNumericDate: Start + id,
        NbOfValidCards: 1,
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

    private static CardRow Card(int panelId, int cardId, int components, long anomalyAr = 0, int cardStatus = 1) => new(
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
