using Nieweb.DataSources;
using Nieweb.Reports.TestKit;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

public sealed class AnalyseLinePerformanceReportTests
{
    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RunAsync_PreReflow_OnlyLastInspection_UsesInMemoryDedupe()
    {
        var descriptor = new SourceDescriptor(
            Id: "prereflow",
            DisplayName: "Pre-reflow AOI",
            SchemaVersion: "4.3.1",
            Caps: Capabilities.PastePrintMetrics);

        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
                Panel(id: 3, machineId: 10, date: start + 15, status: 2, barcode: "BC-1", face: 1),
                Panel(id: 4, machineId: 11, date: start + 30, status: 0, barcode: "BC-2", face: 0),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10),
                Card(panelId: 3, machineId: 10, date: start + 15, nbTestsOnComp: 10),
                Card(panelId: 4, machineId: 11, date: start + 30, nbTestsOnComp: 5),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 1, errorTableAr: 1),
                Obj(panelId: 3, machineId: 10, date: start + 15, objectTypeId: 0x01, errorTable: 2, errorTableAr: 2),
                Obj(panelId: 4, machineId: 11, date: start + 30, objectTypeId: 0x01, errorTable: 0, errorTableAr: 0),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        var result = await AnalyseLinePerformanceReport.Instance.RunAsync(
            source,
            new AnalyseDashboardFilter(_oneDay, OnlyLastInspection: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.DedupeAppliedInMemory);
        Assert.Equal(3, result.OverallYield.TotalPanels);
        Assert.Equal(2, result.OverallYield.InspectedPanels);
        Assert.Equal(100d, result.OverallYield.FpyPercent);
        Assert.Equal(2, result.ByMachine.Count);

        var line10 = Assert.Single(result.ByMachine, r => r.MachineId == 10);
        Assert.Equal(2, line10.Yield.GoodPanels);
        Assert.Equal(20L, line10.Dpmo.OpportunityCount);
        Assert.Equal(2L, line10.Dpmo.DefectBitCount);
        Assert.Equal(100_000d, line10.Dpmo.DpmoPpm);

        SnapshotAssert.Match(result, "AnalyseLinePerformance_PreReflow_Dedupe");
    }

    [Fact]
    public async Task RunAsync_PostReflow_RawMode_DoesNotDedupe()
    {
        var descriptor = new SourceDescriptor(
            Id: "postreflow",
            DisplayName: "Post-reflow AOI",
            SchemaVersion: "5.0",
            Caps: Capabilities.IsLastInspectionFilter);

        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3),
            ],
        };

        var result = await AnalyseLinePerformanceReport.Instance.RunAsync(
            source,
            new AnalyseDashboardFilter(_oneDay, OnlyLastInspection: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.DedupeAppliedInMemory);
        Assert.Equal(2, result.OverallYield.TotalPanels);
        Assert.Equal(1, result.OverallYield.GoodPanels);
        Assert.Equal(1, result.OverallYield.FaultyPanels);
        Assert.Equal(2, result.OverallDpmo.DefectBitCount);
        Assert.Equal(200_000d, result.OverallDpmo.DpmoPpm);
    }

    private static PanelRow Panel(int id, int machineId, int date, int status, string barcode, int face) =>
        new(
            PanelId: id,
            MachineId: machineId,
            LaneNumber: 1,
            PanelBarCode: barcode,
            PanelNumericDate: date,
            NbOfValidCards: 4,
            TestTime: 12,
            PanelStatus: status,
            AnomalyBr: 0,
            AnomalyAr: 0,
            HasBeenReviewed: false,
            NbOfTestedObject: 100,
            NbOfErrorObject: status is -2 or -1 ? 3 : 0,
            OperatorId: 42,
            ProductId: 100,
            RecipeId: 200,
            FaceNumber: face);

    private static CardRow Card(long panelId, int machineId, int date, int nbTestsOnComp) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
            ProductId: 100,
            PanelNumericDate: date,
            NbOfTestsOnComp: nbTestsOnComp,
            NbOfTestsOnPads: null);

    private static TestedObjectRow Obj(long panelId, int machineId, int date, int objectTypeId, long errorTable, long errorTableAr) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            ObjectId: date,
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: 100,
            PanelNumericDate: date,
            Topology: null,
            PartNumberName: null,
            JedecName: null);
}
