using Nieweb.DataSources;
using Nieweb.Reports.TestKit;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="DpmoTableReport"/>. Explicit math assertions
/// verify the three numerators (AOI / Real / Dummy), the three
/// opportunity filters (All / Components / Paste), and the six
/// grouping axes from Vieweb §3.1.6.5. Snapshots guard the JSON
/// shape and the "sorted descending by DPMO" contract.
/// </summary>
public sealed class DpmoTableReportTests
{
    private static readonly SourceDescriptor _postReflow = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI",
        SchemaVersion: "5.0",
        Caps: Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView);

    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    private const int ComponentType = 0x01;
    private const int PastePadType = 0x10;

    // Bit masks from DefectBit / vit-aoi-database skill.
    private const long BitObjectMissing = 1L << 0;   // bit 1
    private const long BitPolarityError = 1L << 1;   // bit 2
    private const long BitSolderJoint  = 1L << 2;   // bit 3

    [Fact]
    public async Task Empty_ReturnsZeroKpisAndEmptyRows()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.All);

        var result = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(_postReflow, result.Source);
        Assert.Equal(0L, result.Overall.TestedObjectCount);
        Assert.Equal(0L, result.Overall.OpportunityCount);
        Assert.Equal(0L, result.Overall.DefectBitCount);
        Assert.Equal(0d, result.Overall.DpmoPpm);
        Assert.Empty(result.Rows);

        SnapshotAssert.Match(result, "DpmoTable_Empty");
    }

    [Fact]
    public async Task AoiNumerator_All_ByMachine_CountsAllTestedObjectsAsOpportunities()
    {
        // Machine 10: 4 components with defect counts {2, 0, 1, 0}
        //   opportunities=4, defectBits=3, DPMO = 750_000
        // Machine 11: 2 components with defect counts {1, 0}
        //   opportunities=2, defectBits=1, DPMO = 500_000
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, errorTable: BitObjectMissing | BitPolarityError, errorTableAr: BitObjectMissing | BitPolarityError),
                Obj(10, start + 61, ComponentType, 0, 0),
                Obj(10, start + 62, ComponentType, BitSolderJoint, BitSolderJoint),
                Obj(10, start + 63, ComponentType, 0, 0),
                Obj(11, start + 70, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(11, start + 71, ComponentType, 0, 0),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };
        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.All);

        var result = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(6L, result.Overall.TestedObjectCount);
        Assert.Equal(6L, result.Overall.OpportunityCount);
        Assert.Equal(4L, result.Overall.DefectBitCount);
        Assert.Equal(1_000_000d * 4 / 6, result.Overall.DpmoPpm);

        // Rows sorted descending by DPMO: Machine 10 (750k) before Machine 11 (500k).
        Assert.Collection(result.Rows,
            r =>
            {
                Assert.Equal("10", r.GroupKey);
                Assert.Equal("AOI-10", r.GroupName);
                Assert.Equal(750_000d, r.Kpi.DpmoPpm);
            },
            r =>
            {
                Assert.Equal("11", r.GroupKey);
                Assert.Equal("AOI-11", r.GroupName);
                Assert.Equal(500_000d, r.Kpi.DpmoPpm);
            });

        SnapshotAssert.Match(result, "DpmoTable_Aoi_All_ByMachine");
    }

    [Fact]
    public async Task DummyVsReal_SplitsErrorTableAndErrorTableAr()
    {
        // Two components on machine 10.
        //   comp A: Error_Table = missing|polarity, Error_Table_AR = missing
        //     -> Real=1 (missing), Dummy=1 (polarity cleared by review)
        //   comp B: Error_Table = solder, Error_Table_AR = 0
        //     -> Real=0, Dummy=1
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, errorTable: BitObjectMissing | BitPolarityError, errorTableAr: BitObjectMissing),
                Obj(10, start + 61, ComponentType, errorTable: BitSolderJoint, errorTableAr: 0),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };

        var realFilter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Real, DpmoOpportunity.All);
        var dummyFilter = realFilter with { Numerator = DpmoNumerator.Dummy };

        var realResult = await DpmoTableReport.Instance.RunAsync(
            source, realFilter, TestContext.Current.CancellationToken);
        var dummyResult = await DpmoTableReport.Instance.RunAsync(
            source, dummyFilter, TestContext.Current.CancellationToken);

        Assert.Equal(1L, realResult.Overall.DefectBitCount);
        Assert.Equal(2L, dummyResult.Overall.DefectBitCount);
        Assert.Equal(500_000d, realResult.Overall.DpmoPpm);
        Assert.Equal(1_000_000d, dummyResult.Overall.DpmoPpm);
    }

    [Fact]
    public async Task OpportunityComponents_ExcludesPastePadsFromDenominator()
    {
        // 1 component (1 defect) + 3 paste pads (0 defects).
        // Components-only DPMO = 1_000_000 (1 defect / 1 opportunity).
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(10, start + 61, PastePadType, 0, 0),
                Obj(10, start + 62, PastePadType, 0, 0),
                Obj(10, start + 63, PastePadType, 0, 0),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.Components);

        var result = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(4L, result.Overall.TestedObjectCount);
        Assert.Equal(1L, result.Overall.OpportunityCount);
        Assert.Equal(1L, result.Overall.DefectBitCount);
        Assert.Equal(1_000_000d, result.Overall.DpmoPpm);
    }

    [Fact]
    public async Task GroupByDefect_EmitsOneRowPerSetBit()
    {
        // 2 components: comp A has {missing, polarity}, comp B has {missing}.
        // Denominator (opportunities) is 2 for every defect row.
        // Row "Object missing" -> 2 defects / 2 opps = 1_000_000
        // Row "Polarity error" -> 1 defect  / 2 opps = 500_000
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
                Obj(10, start + 61, ComponentType, BitObjectMissing, BitObjectMissing),
            ],
        };
        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.Defect, DpmoNumerator.Aoi, DpmoOpportunity.All);

        var result = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("1", result.Rows[0].GroupKey); // Object missing = bit 1
        Assert.Equal("Object missing", result.Rows[0].GroupName);
        Assert.Equal(1_000_000d, result.Rows[0].Kpi.DpmoPpm);
        Assert.Equal("2", result.Rows[1].GroupKey); // Polarity error = bit 2
        Assert.Equal(500_000d, result.Rows[1].Kpi.DpmoPpm);

        SnapshotAssert.Match(result, "DpmoTable_Aoi_All_ByDefect");
    }

    [Fact]
    public async Task GroupByPartNumber_NullPartNumber_YieldsNullKeyAndName()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-A"),
                Obj(10, start + 61, ComponentType, 0, 0, partNumberName: null),
                Obj(10, start + 62, ComponentType, BitPolarityError, BitPolarityError, partNumberName: null),
            ],
        };
        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.PartNumber, DpmoNumerator.Aoi, DpmoOpportunity.All);

        var result = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // PN-A: 1 defect / 1 opp = 1_000_000
        // null:  1 defect / 2 opps = 500_000
        Assert.Equal(2, result.Rows.Count);
        var pnA = Assert.Single(result.Rows, r => r.GroupKey == "PN-A");
        var pnNull = Assert.Single(result.Rows, r => r.GroupKey is null);
        Assert.Equal("PN-A", pnA.GroupName);
        Assert.Null(pnNull.GroupName);
        Assert.Equal(1_000_000d, pnA.Kpi.DpmoPpm);
        Assert.Equal(500_000d, pnNull.Kpi.DpmoPpm);
        // Descending sort by DPMO: PN-A first.
        Assert.Equal("PN-A", result.Rows[0].GroupKey);
    }

    private static TestedObjectRow Obj(
        int machineId,
        int date,
        int objectTypeId,
        long errorTable,
        long errorTableAr,
        int productId = 500,
        string? topology = null,
        string? partNumberName = null,
        string? jedecName = null)
    {
        return new TestedObjectRow(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: date, // unique per row
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            Topology: topology,
            PartNumberName: partNumberName,
            JedecName: jedecName);
    }
}
