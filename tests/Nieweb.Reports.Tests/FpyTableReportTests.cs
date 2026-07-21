using Nieweb.DataSources;
using Nieweb.Reports.TestKit;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="FpyTableReport"/>. Explicit math assertions
/// verify the three FPY flavours (AOI / Diagnostic / After Repair) at
/// row and grand-total level; snapshots guard the JSON shape and the
/// "ordered by increasing FPY" contract from Vieweb §3.1.6.4.
/// </summary>
public sealed class FpyTableReportTests
{
    private static readonly SourceDescriptor _postReflow = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI",
        SchemaVersion: "5.0",
        Caps: Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView);

    private static readonly SourceDescriptor _preReflow = new(
        Id: "prereflow",
        DisplayName: "Pre-reflow AOI",
        SchemaVersion: "4.3.1",
        Caps: Capabilities.PastePrintMetrics | Capabilities.FeederAnalytics);

    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Panel_ByMachine_EmptyWindow_ReturnsZeroKpisAndEmptyRows()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(_postReflow, result.Source);
        Assert.Equal(FpyGranularity.Panel, result.Granularity);
        Assert.Equal(FpyGroupBy.AoiMachine, result.GroupBy);
        Assert.Equal(0, result.Overall.InspectedCount);
        Assert.Equal(0d, result.Overall.FpyAoiPercent);
        Assert.Equal(0d, result.Overall.FpyDiagnosticPercent);
        Assert.Equal(0d, result.Overall.FpyAfterRepairPercent);
        Assert.Empty(result.Rows);

        SnapshotAssert.Match(result, "FpyTable_Panel_ByMachine_Empty");
    }

    [Fact]
    public async Task Panel_ByMachine_ThreeFpyFlavoursAcrossAllStatusCodes()
    {
        // Machine 10: status {1, 1, 2, 3, -1, 0}
        //   Inspected=5, GoodAoi=2, GoodDiag=3 (1+1+2), GoodAr=4 (1+1+2+3), Faulty=1, NotInspected=1
        //   FpyAoi = 40%, FpyDiag = 60%, FpyAr = 80%
        // Machine 11: status {1, 2, 2, -2}
        //   Inspected=4, GoodAoi=1, GoodDiag=3 (1+2+2), GoodAr=3, Faulty=1
        //   FpyAoi = 25%, FpyDiag = 75%, FpyAr = 75%
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 60, status: 1, productId: 500),
                Panel(id: 2, machineId: 10, date: start + 120, status: 1, productId: 500),
                Panel(id: 3, machineId: 10, date: start + 180, status: 2, productId: 500),
                Panel(id: 4, machineId: 10, date: start + 240, status: 3, productId: 500),
                Panel(id: 5, machineId: 10, date: start + 300, status: -1, productId: 500),
                Panel(id: 6, machineId: 10, date: start + 360, status: 0, productId: 500),
                Panel(id: 7, machineId: 11, date: start + 60, status: 1, productId: 501),
                Panel(id: 8, machineId: 11, date: start + 120, status: 2, productId: 501),
                Panel(id: 9, machineId: 11, date: start + 180, status: 2, productId: 501),
                Panel(id: 10, machineId: 11, date: start + 240, status: -2, productId: 501),
            ],
            SeededMachines =
            [
                new Machine(MachineId: 10, MachineType: 2, MachineName: "AOI-10", MachineTypeName: "AOI"),
                new Machine(MachineId: 11, MachineType: 2, MachineName: "AOI-11", MachineTypeName: "AOI"),
            ],
        };
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // Overall: 10 rows total, 9 inspected (excluding one not-inspected),
        // GoodAoi = 3 (1+1+1), GoodDiag = 6 (3 + 3 more status=2), GoodAr = 7 (+status=3).
        Assert.Equal(10, result.Overall.TotalRows);
        Assert.Equal(9, result.Overall.InspectedCount);
        Assert.Equal(1, result.Overall.NotInspectedCount);
        Assert.Equal(2, result.Overall.FaultyCount);
        Assert.Equal(3, result.Overall.GoodAoiCount);
        Assert.Equal(6, result.Overall.GoodDiagnosticCount);
        Assert.Equal(7, result.Overall.GoodAfterRepairCount);
        Assert.Equal(100d * 3 / 9, result.Overall.FpyAoiPercent);
        Assert.Equal(100d * 6 / 9, result.Overall.FpyDiagnosticPercent);
        Assert.Equal(100d * 7 / 9, result.Overall.FpyAfterRepairPercent);

        // Rows sorted by ascending FpyAoiPercent: Machine 11 (25%) then Machine 10 (40%).
        Assert.Collection(result.Rows,
            r =>
            {
                Assert.Equal(11, r.GroupKey);
                Assert.Equal("AOI-11", r.GroupName);
                Assert.Equal(25d, r.Kpi.FpyAoiPercent);
                Assert.Equal(75d, r.Kpi.FpyDiagnosticPercent);
                Assert.Equal(75d, r.Kpi.FpyAfterRepairPercent);
            },
            r =>
            {
                Assert.Equal(10, r.GroupKey);
                Assert.Equal("AOI-10", r.GroupName);
                Assert.Equal(40d, r.Kpi.FpyAoiPercent);
                Assert.Equal(60d, r.Kpi.FpyDiagnosticPercent);
                Assert.Equal(80d, r.Kpi.FpyAfterRepairPercent);
            });

        SnapshotAssert.Match(result, "FpyTable_Panel_ByMachine_Mixed");
    }

    [Fact]
    public async Task Panel_ByProduct_ResolvesProductNames_AndSortsByFpyAscending()
    {
        // Product 500: {1, -1} -> FpyAoi = 50%
        // Product 501: {1, 1, 1, -1} -> FpyAoi = 75%
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(1, 10, start + 60, 1, productId: 500),
                Panel(2, 10, start + 120, -1, productId: 500),
                Panel(3, 10, start + 180, 1, productId: 501),
                Panel(4, 10, start + 240, 1, productId: 501),
                Panel(5, 10, start + 300, 1, productId: 501),
                Panel(6, 10, start + 360, -1, productId: 501),
            ],
            SeededProducts =
            [
                new Product(ProductId: 500, ProductName: "Prod-A", Revision: null, Description: null),
                new Product(ProductId: 501, ProductName: "Prod-B", Revision: null, Description: null),
            ],
        };
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.Product);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Collection(result.Rows,
            r =>
            {
                Assert.Equal(500, r.GroupKey);
                Assert.Equal("Prod-A", r.GroupName);
                Assert.Equal(50d, r.Kpi.FpyAoiPercent);
            },
            r =>
            {
                Assert.Equal(501, r.GroupKey);
                Assert.Equal("Prod-B", r.GroupName);
                Assert.Equal(75d, r.Kpi.FpyAoiPercent);
            });

        SnapshotAssert.Match(result, "FpyTable_Panel_ByProduct_Sorted");
    }

    [Fact]
    public async Task Panel_UnknownMachineAndProduct_YieldsNullNames()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(1, machineId: 99, date: start + 60, status: 1, productId: 999),
            ],
            // Empty catalogues on purpose.
        };
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Rows);
        Assert.Equal(99, only.GroupKey);
        Assert.Null(only.GroupName);
    }

    [Fact]
    public async Task Board_ByMachine_UsesCardStatusNotPanelStatus()
    {
        // Panel A (machine 10) is Panel_Status=1 overall but has 2 cards:
        //   card 1 = 1 (good), card 2 = -1 (faulty). Board-level FPY = 50%.
        // Panel B (machine 10) is Panel_Status=-1 but has 1 card = 1.
        // Board-level FPY on machine 10 = 2 good / 3 inspected = 66.67%.
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(panelId: 1, cardId: 1, machineId: 10, productId: 500, status: 1, date: start + 60),
                Card(panelId: 1, cardId: 2, machineId: 10, productId: 500, status: -1, date: start + 60),
                Card(panelId: 2, cardId: 1, machineId: 10, productId: 500, status: 1, date: start + 120),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
            ],
        };
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Board, FpyGroupBy.AoiMachine);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(FpyGranularity.Board, result.Granularity);
        Assert.Equal(3, result.Overall.TotalRows);
        Assert.Equal(3, result.Overall.InspectedCount);
        Assert.Equal(2, result.Overall.GoodAoiCount);
        Assert.Equal(1, result.Overall.FaultyCount);
        Assert.Equal(100d * 2 / 3, result.Overall.FpyAoiPercent);

        SnapshotAssert.Match(result, "FpyTable_Board_ByMachine_MixedStatuses");
    }

    [Fact]
    public async Task Board_ByProduct_HonoursCardWindowAndFilters()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var end = (int)_oneDay.EndEpochSecondsExclusive;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(1, 1, 10, 500, 1, start - 1),        // out - before
                Card(2, 1, 10, 500, 1, start),            // in - start inclusive
                Card(3, 1, 10, 500, -1, end - 1),         // in - end exclusive
                Card(4, 1, 10, 500, 1, end),              // out - at end
                Card(5, 1, 10, 501, 1, start + 60),       // filtered by ProductIds
            ],
            SeededProducts =
            [
                new Product(500, "Prod-A", null, null),
                new Product(501, "Prod-B", null, null),
            ],
        };
        var filter = new FpyTableFilter(
            _oneDay, FpyGranularity.Board, FpyGroupBy.Product,
            ProductIds: [500]);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Overall.TotalRows);
        var only = Assert.Single(result.Rows);
        Assert.Equal(500, only.GroupKey);
        Assert.Equal("Prod-A", only.GroupName);
        Assert.Equal(50d, only.Kpi.FpyAoiPercent);
    }

    [Fact]
    public async Task Panel_PreReflow_Status3IsCountedAfterRepairOnly()
    {
        // Pre-reflow schema v4.3.1 lets Panel_Status = 3 (good after
        // review) appear. AOI FPY must NOT include it; After Repair must.
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_preReflow)
        {
            SeededPanels =
            [
                Panel(1, 20, start + 60, 1, productId: 500),
                Panel(2, 20, start + 120, 3, productId: 500),
                Panel(3, 20, start + 180, -1, productId: 500),
            ],
            SeededMachines =
            [
                new Machine(20, 2, "AOI-20", "AOI"),
            ],
        };
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // Inspected = 3. GoodAoi = 1 (status 1 only). GoodDiag = 1 (no
        // status-2). GoodAr = 2 (status 1 and 3).
        Assert.Equal(3, result.Overall.InspectedCount);
        Assert.Equal(1, result.Overall.GoodAoiCount);
        Assert.Equal(1, result.Overall.GoodDiagnosticCount);
        Assert.Equal(2, result.Overall.GoodAfterRepairCount);
        Assert.Equal(100d / 3, result.Overall.FpyAoiPercent);
        Assert.Equal(100d / 3, result.Overall.FpyDiagnosticPercent);
        Assert.Equal(200d / 3, result.Overall.FpyAfterRepairPercent);

        SnapshotAssert.Match(result, "FpyTable_Panel_PreReflow_Status3");
    }

    [Fact]
    public async Task Panel_UnknownStatusCounted_AsNotInspected()
    {
        // Superviseur canonical enum is {-2,-1,0,1,2,3}. A row with an
        // out-of-band value must fall into not-inspected so FPY stays
        // honest — otherwise a schema change could silently inflate FPY.
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(1, 10, start + 60, 1, productId: 500),
                Panel(2, 10, start + 120, 42, productId: 500), // bogus status
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
            ],
        };
        var filter = new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine);

        var result = await FpyTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Overall.TotalRows);
        Assert.Equal(1, result.Overall.InspectedCount);
        Assert.Equal(1, result.Overall.NotInspectedCount);
        Assert.Equal(100d, result.Overall.FpyAoiPercent);
    }

    private static PanelRow Panel(int id, int machineId, int date, int status, int productId) =>
        new(
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
            HasBeenReviewed: false,
            NbOfTestedObject: 100,
            NbOfErrorObject: status is (-2) or (-1) ? 3 : 0,
            OperatorId: 42,
            ProductId: productId,
            RecipeId: 600);

    private static CardRow Card(int panelId, int cardId, int machineId, int productId, int status, int date) =>
        new(
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
}
