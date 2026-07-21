using Nieweb.DataSources;
using Nieweb.Reports.Tests.Fakes;
using Nieweb.Reports.Tests.Snapshots;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="PanelYieldByLineReport"/>. Combines a small
/// number of explicit KPI assertions (for math traceability) with
/// JSON snapshots (as regression guards against accidental shape or
/// ordering changes).
/// </summary>
public sealed class PanelYieldByLineReportTests
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

    // Window: 2026-01-01 00:00:00 UTC -> 2026-01-02 00:00:00 UTC.
    // Chosen so the epoch seconds render as clean, stable numbers in
    // snapshots (StartEpochSeconds = 1767225600).
    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RunAsync_WithNoPanels_ReturnsZeroKpisAndEmptyByMachine()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new PanelYieldFilter(_oneDay);

        var result = await PanelYieldByLineReport.RunAsync(source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(_postReflow, result.Source);
        Assert.Equal(_oneDay, result.Window);
        Assert.Equal(0, result.Overall.TotalPanels);
        Assert.Equal(0, result.Overall.InspectedPanels);
        Assert.Equal(0d, result.Overall.FpyPercent);
        Assert.Empty(result.ByMachine);

        SnapshotAssert.Match(result, "PanelYield_Empty_PostReflow");
    }

    [Fact]
    public async Task RunAsync_PostReflowMixedStatuses_ComputesFpyPerMachine()
    {
        // Machine 10 (SPI-A): 3 good (1,1,2), 1 faulty (-1), 1 not-inspected (0)
        //   -> Inspected=4, FPY = 3/4 = 75%
        // Machine 11 (AOI-B): 2 good (1,2), 2 faulty (-2,-1)
        //   -> Inspected=4, FPY = 2/4 = 50%
        // Overall: 5 good, 3 faulty, 1 not-inspected. FPY = 5/8 = 62.5%.
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 60, status: 1),
                Panel(id: 2, machineId: 10, date: start + 120, status: 1),
                Panel(id: 3, machineId: 10, date: start + 180, status: 2),
                Panel(id: 4, machineId: 10, date: start + 240, status: -1),
                Panel(id: 5, machineId: 10, date: start + 300, status: 0),
                Panel(id: 6, machineId: 11, date: start + 60, status: 1),
                Panel(id: 7, machineId: 11, date: start + 120, status: 2),
                Panel(id: 8, machineId: 11, date: start + 180, status: -2),
                Panel(id: 9, machineId: 11, date: start + 240, status: -1),
            ],
            SeededMachines =
            [
                new Machine(MachineId: 10, MachineType: 1, MachineName: "SPI-A", MachineTypeName: "SPI"),
                new Machine(MachineId: 11, MachineType: 2, MachineName: "AOI-B", MachineTypeName: "AOI"),
            ],
        };
        var filter = new PanelYieldFilter(_oneDay);

        var result = await PanelYieldByLineReport.RunAsync(source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(9, result.Overall.TotalPanels);
        Assert.Equal(8, result.Overall.InspectedPanels);
        Assert.Equal(5, result.Overall.GoodPanels);
        Assert.Equal(3, result.Overall.FaultyPanels);
        Assert.Equal(1, result.Overall.NotInspectedPanels);
        Assert.Equal(62.5d, result.Overall.FpyPercent);

        Assert.Collection(result.ByMachine,
            m =>
            {
                Assert.Equal(10, m.MachineId);
                Assert.Equal("SPI-A", m.MachineName);
                Assert.Equal(75d, m.Kpi.FpyPercent);
            },
            m =>
            {
                Assert.Equal(11, m.MachineId);
                Assert.Equal("AOI-B", m.MachineName);
                Assert.Equal(50d, m.Kpi.FpyPercent);
            });

        SnapshotAssert.Match(result, "PanelYield_Mixed_PostReflow");
    }

    [Fact]
    public async Task RunAsync_PreReflowStatus3IsGood_AndUnknownMachineGetsNullName()
    {
        // Pre-reflow schema v4.3.1 adds Panel_Status=3 to the good set.
        // Machine 22 appears in PANELS but not in the MACHINE catalogue,
        // so MachineName should be null (a real corner case for
        // decommissioned inspectors).
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_preReflow)
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 21, date: start + 30, status: 3),  // good (pre-reflow only)
                Panel(id: 2, machineId: 21, date: start + 90, status: 1),  // good
                Panel(id: 3, machineId: 21, date: start + 150, status: -2), // faulty
                Panel(id: 4, machineId: 22, date: start + 60, status: 2),  // good, unknown machine
                Panel(id: 5, machineId: 22, date: start + 120, status: 0), // not-inspected
            ],
            SeededMachines =
            [
                new Machine(MachineId: 21, MachineType: 2, MachineName: "AOI-Pre-1", MachineTypeName: "AOI"),
            ],
        };
        var filter = new PanelYieldFilter(_oneDay);

        var result = await PanelYieldByLineReport.RunAsync(source, filter, TestContext.Current.CancellationToken);

        // Statuses: {3, 1, -2, 2, 0} -> good {3,1,2}=3, faulty {-2}=1, not-inspected {0}=1.
        // FPY = 3 / 4 = 75%.
        Assert.Equal(3, result.Overall.GoodPanels);
        Assert.Equal(1, result.Overall.FaultyPanels);
        Assert.Equal(1, result.Overall.NotInspectedPanels);
        Assert.Equal(75d, result.Overall.FpyPercent);

        var m22 = Assert.Single(result.ByMachine, m => m.MachineId == 22);
        Assert.Null(m22.MachineName);

        SnapshotAssert.Match(result, "PanelYield_PreReflow_Status3_UnknownMachine");
    }

    [Fact]
    public async Task RunAsync_FilterHonoursMachineIdsAndWindowBoundaries()
    {
        // Verify that filter application is delegated correctly through
        // PanelQuery -> StreamPanelsAsync (fake source enforces the
        // same half-open window + set membership contract as the real
        // SQL source).
        var start = (int)_oneDay.StartEpochSeconds;
        var end = (int)_oneDay.EndEpochSecondsExclusive;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 30, date: start - 1, status: 1),   // out (before)
                Panel(id: 2, machineId: 30, date: start, status: 1),       // in (start inclusive)
                Panel(id: 3, machineId: 30, date: end - 1, status: 1),     // in (end exclusive)
                Panel(id: 4, machineId: 30, date: end, status: 1),         // out (at end)
                Panel(id: 5, machineId: 31, date: start + 60, status: 1),  // filtered out by MachineIds
            ],
            SeededMachines =
            [
                new Machine(30, 2, "AOI-30", "AOI"),
                new Machine(31, 2, "AOI-31", "AOI"),
            ],
        };
        var filter = new PanelYieldFilter(_oneDay, MachineIds: [30]);

        var result = await PanelYieldByLineReport.RunAsync(source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Overall.TotalPanels);
        Assert.Equal(2, result.Overall.GoodPanels);
        var only = Assert.Single(result.ByMachine);
        Assert.Equal(30, only.MachineId);
    }

    /// <summary>Builds a minimal PanelRow with sensible defaults so tests can focus on the fields under test.</summary>
    private static PanelRow Panel(int id, int machineId, int date, int status) =>
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
            ProductId: 500,
            RecipeId: 600);
}
