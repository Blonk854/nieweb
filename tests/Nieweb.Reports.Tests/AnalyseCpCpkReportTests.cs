using Nieweb.DataSources;
using Nieweb.Reports.TestKit;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

public sealed class AnalyseCpCpkReportTests
{
    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RunAsync_ComputesCpCpk_WhenToleranceConfigured()
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
                Panel(id: 1, productId: 100, machineId: 10, date: start + 10, status: 1, barcode: "BC-1", face: 0),
            ],
            SeededTestedObjects =
            [
                // Symmetric samples around 0: mean 0, sample std-dev known.
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: -10),
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: -5),
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: 0),
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: 5),
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: 10),
            ],
        };

        // IT = 60µm full interval, centered mean 0 → Cp = Cpk = 60/(6σ).
        // Sample std-dev of {-10,-5,0,5,10} = sqrt(125/2)... verify via report.
        var filter = new AnalyseCpCpkFilter(_oneDay, OnlyLastInspection: false, ComponentItx: 60);
        var result = await AnalyseCpCpkReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.False(result.DedupeAppliedInMemory);
        var row = Assert.Single(result.Rows, r => r.Axis == DeviationAxis.DeltaX && r.Opportunity == DpmoOpportunity.Components);
        Assert.Equal(5, row.SampleCount);
        Assert.True(row.ToleranceConfigured);
        Assert.NotNull(row.Cp);
        Assert.NotNull(row.Cpk);
        // Centered → Cp == Cpk.
        Assert.Equal(row.Cp!.Value, row.Cpk!.Value, precision: 9);
        // σ = sqrt(62.5) ≈ 7.9057 → Cp = 60/(6·7.9057) ≈ 1.2649.
        Assert.Equal(1.264911064, row.Cp!.Value, precision: 6);

        SnapshotAssert.Match(result, "AnalyseCpCpk_Configured");
    }

    [Fact]
    public async Task RunAsync_MissingTolerance_ReportsNotConfigured()
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
                Panel(id: 1, productId: 100, machineId: 10, date: start + 10, status: 1, barcode: "BC-1", face: 0),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: 3),
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: 5),
            ],
        };

        var result = await AnalyseCpCpkReport.Instance.RunAsync(
            source, new AnalyseCpCpkFilter(_oneDay, OnlyLastInspection: false),
            TestContext.Current.CancellationToken);

        var row = Assert.Single(result.Rows, r => r.Axis == DeviationAxis.DeltaX && r.Opportunity == DpmoOpportunity.Components);
        Assert.Equal(2, row.SampleCount);
        Assert.False(row.ToleranceConfigured);
        Assert.Null(row.Cp);
        Assert.Null(row.Cpk);
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
                Panel(id: 1, productId: 100, machineId: 10, date: start + 10, status: 1, barcode: "BC-1", face: 0),
                Panel(id: 2, productId: 100, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 1, productId: 100, machineId: 10, date: start + 10, objectTypeId: 0x01, deltaX: 100),
                Obj(panelId: 2, productId: 100, machineId: 10, date: start + 20, objectTypeId: 0x01, deltaX: 4),
                Obj(panelId: 2, productId: 100, machineId: 10, date: start + 20, objectTypeId: 0x01, deltaX: 6),
            ],
        };

        var result = await AnalyseCpCpkReport.Instance.RunAsync(
            source, new AnalyseCpCpkFilter(_oneDay, OnlyLastInspection: true, ComponentItx: 60),
            TestContext.Current.CancellationToken);

        Assert.True(result.DedupeAppliedInMemory);
        var row = Assert.Single(result.Rows, r => r.Axis == DeviationAxis.DeltaX && r.Opportunity == DpmoOpportunity.Components);
        // Only panel 2 survives dedupe → samples {4, 6}, mean 5.
        Assert.Equal(2, row.SampleCount);
        Assert.NotNull(row.Mean);
        Assert.Equal(5, row.Mean!.Value, precision: 9);
    }

    private static PanelRow Panel(int id, int productId, int machineId, int date, int status, string barcode, int face) =>
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
            NbOfErrorObject: 0,
            OperatorId: 42,
            ProductId: productId,
            RecipeId: 200,
            FaceNumber: face);

    private static TestedObjectRow Obj(long panelId, int productId, int machineId, int date, int objectTypeId, double deltaX) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            ObjectId: date,
            ObjectTypeId: objectTypeId,
            ErrorTable: 0,
            ErrorTableAr: 0,
            Status: 0,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            Topology: null,
            PartNumberName: null,
            JedecName: null,
            DeltaXUm: deltaX);
}
