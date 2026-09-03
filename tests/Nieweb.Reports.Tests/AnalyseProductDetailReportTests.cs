using System.Globalization;

using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

public sealed class AnalyseProductDetailReportTests
{
    [Fact]
    public async Task PreReflow_OnlyLastInspection_AppliesInMemoryDedupe_AndBuildsTrend()
    {
        var start = DateTimeOffset.Parse("2026-08-01T00:00:00Z", CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse("2026-08-03T00:00:00Z", CultureInfo.InvariantCulture);

        var source = new FakeAoiSource(
            new SourceDescriptor("pre", "Pre", "4.3.1", Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                // Same barcode+face, newer panel should win when deduping.
                Panel(id: 1, machineId: 10, date: "2026-08-01T08:00:00Z", status: -1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 2, machineId: 10, date: "2026-08-01T09:00:00Z", status: 1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 3, machineId: 10, date: "2026-08-02T09:00:00Z", status: 2, barcode: "BC-2", face: 0, productId: 100),
            ],
            SeededCards =
            [
                Card(panelId: 1, machineId: 10, date: "2026-08-01T08:00:00Z", testsOnComp: 10, productId: 100),
                Card(panelId: 2, machineId: 10, date: "2026-08-01T09:00:00Z", testsOnComp: 10, productId: 100),
                Card(panelId: 3, machineId: 10, date: "2026-08-02T09:00:00Z", testsOnComp: 5, productId: 100),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 1, machineId: 10, date: "2026-08-01T08:00:00Z", errorTable: 7, productId: 100),
                Obj(panelId: 2, machineId: 10, date: "2026-08-01T09:00:00Z", errorTable: 3, productId: 100),
                Obj(panelId: 3, machineId: 10, date: "2026-08-02T09:00:00Z", errorTable: 1, productId: 100),
            ],
            SeededProducts =
            [
                new Product(100, "Widget", null, null),
            ],
        };

        var filter = new AnalyseProductDetailFilter(
            Window: new DateRange(start, end),
            ProductId: 100,
            Bucket: TimeBucket.Day,
            OnlyLastInspection: true);

        var result = await AnalyseProductDetailReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.True(result.DedupeAppliedInMemory);
        Assert.Equal(2, result.OverallYield.TotalPanels);
        Assert.Equal(2, result.OverallYield.GoodPanels);
        Assert.Equal(0, result.OverallYield.FaultyPanels);
        Assert.Equal(15, result.OverallDpmo.OpportunityCount);
        Assert.Equal(3, result.OverallDpmo.DefectBitCount);
        Assert.Equal(2, result.Buckets.Count);
        Assert.Equal(2, result.Trend.Count);
        Assert.Equal("Widget", result.ProductName);
        Assert.Equal(2, result.TopDefectBits.Count);
        Assert.Equal(1, result.TopDefectBits[0].BitNumber);
        Assert.Equal(2, result.TopDefectBits[0].Count);
        Assert.Equal(2, result.Trend[0].TopDefectBits.Count);
        Assert.Equal(1, result.Trend[0].TopDefectBits[0].BitNumber);
        Assert.Equal(1, result.Trend[0].TopDefectBits[0].Count);
        Assert.Equal(2, result.Trend[0].TopDefectBits[1].BitNumber);
        Assert.Equal(1, result.Trend[0].TopDefectBits[1].Count);
    }

    [Fact]
    public async Task PostReflow_OnlyLastInspection_DoesNotUseInMemoryDedupe()
    {
        var start = DateTimeOffset.Parse("2026-08-01T00:00:00Z", CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse("2026-08-02T00:00:00Z", CultureInfo.InvariantCulture);

        var source = new FakeAoiSource(
            new SourceDescriptor("post", "Post", "5.0", Capabilities.IsLastInspectionFilter))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: "2026-08-01T08:00:00Z", status: -1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 2, machineId: 10, date: "2026-08-01T09:00:00Z", status: 1, barcode: "BC-1", face: 0, productId: 100),
            ],
            SeededCards =
            [
                Card(panelId: 1, machineId: 10, date: "2026-08-01T08:00:00Z", testsOnComp: 10, productId: 100),
                Card(panelId: 2, machineId: 10, date: "2026-08-01T09:00:00Z", testsOnComp: 10, productId: 100),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 1, machineId: 10, date: "2026-08-01T08:00:00Z", errorTable: 1, productId: 100),
                Obj(panelId: 2, machineId: 10, date: "2026-08-01T09:00:00Z", errorTable: 3, productId: 100),
            ],
            SeededProducts =
            [
                new Product(100, "Widget", null, null),
            ],
        };

        var filter = new AnalyseProductDetailFilter(
            Window: new DateRange(start, end),
            ProductId: 100,
            Bucket: TimeBucket.Day,
            OnlyLastInspection: true);

        var result = await AnalyseProductDetailReport.Instance.RunAsync(source, filter, CancellationToken.None);

        Assert.False(result.DedupeAppliedInMemory);
        Assert.Equal(2, result.OverallYield.TotalPanels);
        Assert.Equal(1, result.OverallYield.GoodPanels);
        Assert.Equal(1, result.OverallYield.FaultyPanels);
        Assert.Equal(20, result.OverallDpmo.OpportunityCount);
        Assert.Equal(3, result.OverallDpmo.DefectBitCount);
        Assert.Equal(2, result.Trend[0].TopDefectBits.Count);
    }

    private static PanelRow Panel(int id, int machineId, string date, int status, string barcode, int face, int productId) =>
        new(
            PanelId: id,
            MachineId: machineId,
            LaneNumber: 1,
            PanelBarCode: barcode,
            PanelNumericDate: (int)DateTimeOffset.Parse(date, CultureInfo.InvariantCulture).ToUnixTimeSeconds(),
            NbOfValidCards: 1,
            TestTime: 5,
            PanelStatus: status,
            AnomalyBr: 0,
            AnomalyAr: 0,
            HasBeenReviewed: false,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            OperatorId: null,
            ProductId: productId,
            RecipeId: 1,
            FaceNumber: face);

    private static CardRow Card(long panelId, int machineId, string date, int testsOnComp, int productId) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: (int)DateTimeOffset.Parse(date, CultureInfo.InvariantCulture).ToUnixTimeSeconds(),
            NbOfTestsOnComp: testsOnComp,
            NbOfTestsOnPads: null);

    private static TestedObjectRow Obj(long panelId, int machineId, string date, long errorTable, int productId) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            ObjectId: 1,
            ObjectTypeId: 0x01,
            ErrorTable: errorTable,
            ErrorTableAr: errorTable,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: (int)DateTimeOffset.Parse(date, CultureInfo.InvariantCulture).ToUnixTimeSeconds(),
            Topology: null,
            PartNumberName: null,
            JedecName: null);
}
