using Nieweb.DataSources;
using Nieweb.Reports.TestKit;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

public sealed class AnalyseLiveSummaryReportTests
{
    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RunAsync_NoRows_ReturnsZeroKpis()
    {
        var source = new FakeAoiSource(new SourceDescriptor(
            Id: "postreflow",
            DisplayName: "Post-reflow AOI",
            SchemaVersion: "5.0",
            Caps: Capabilities.IsLastInspectionFilter));

        var result = await AnalyseLiveSummaryReport.Instance.RunAsync(
            source,
            new AnalyseDashboardFilter(_oneDay),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Kpi.TotalPanels);
        Assert.Equal(0d, result.Kpi.FpyPercent);
        Assert.False(result.DedupeAppliedInMemory);
        Assert.Null(result.DedupeNote);

        SnapshotAssert.Match(result, "AnalyseLiveSummary_Empty");
    }

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
                // Same barcode+face inspected twice; latest row is good.
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),

                // Different face of the same barcode counts separately.
                Panel(id: 3, machineId: 10, date: start + 15, status: 2, barcode: "BC-1", face: 1),

                // Independent panel.
                Panel(id: 4, machineId: 10, date: start + 30, status: 0, barcode: "BC-2", face: 0),
            ],
        };

        var result = await AnalyseLiveSummaryReport.Instance.RunAsync(
            source,
            new AnalyseDashboardFilter(_oneDay, OnlyLastInspection: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.DedupeAppliedInMemory);
        Assert.NotNull(result.DedupeNote);

        // After dedupe keys are: (BC-1,0)=>good, (BC-1,1)=>good, (BC-2,0)=>not inspected.
        Assert.Equal(3, result.Kpi.TotalPanels);
        Assert.Equal(2, result.Kpi.InspectedPanels);
        Assert.Equal(2, result.Kpi.GoodPanels);
        Assert.Equal(0, result.Kpi.FaultyPanels);
        Assert.Equal(1, result.Kpi.NotInspectedPanels);
        Assert.Equal(100d, result.Kpi.FpyPercent);

        SnapshotAssert.Match(result, "AnalyseLiveSummary_PreReflow_Dedupe");
    }

    [Fact]
    public async Task RunAsync_PreReflow_RawMode_DoesNotDedupe()
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
            ],
        };

        var result = await AnalyseLiveSummaryReport.Instance.RunAsync(
            source,
            new AnalyseDashboardFilter(_oneDay, OnlyLastInspection: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.DedupeAppliedInMemory);
        Assert.Equal(2, result.Kpi.TotalPanels);
        Assert.Equal(2, result.Kpi.InspectedPanels);
        Assert.Equal(1, result.Kpi.GoodPanels);
        Assert.Equal(1, result.Kpi.FaultyPanels);
        Assert.Equal(50d, result.Kpi.FpyPercent);
    }

    private static PanelRow Panel(
        int id,
        int machineId,
        int date,
        int status,
        string barcode,
        int face) =>
        new(
            PanelId: id,
            MachineId: machineId,
            LaneNumber: 1,
            PanelBarCode: barcode,
            PanelNumericDate: date,
            NbOfValidCards: 4,
            TestTime: 8,
            PanelStatus: status,
            AnomalyBr: 0,
            AnomalyAr: 0,
            HasBeenReviewed: false,
            NbOfTestedObject: 100,
            NbOfErrorObject: status is -2 or -1 ? 3 : 0,
            OperatorId: 7,
            ProductId: 100,
            RecipeId: 200,
            FaceNumber: face);
}
