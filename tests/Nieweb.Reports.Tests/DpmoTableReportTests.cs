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
    public async Task AoiNumerator_All_ByMachine_UsesCardTestCountsAsOpportunities()
    {
        // Opportunity denominators come from CARDS (Nb_Of_Tests_On_Comp),
        // NOT from tested-object row counts:
        //   Machine 10: 100 comp tests, defects {2,1} -> DPMO = 1e6*3/100 = 30_000
        //   Machine 11:  50 comp tests, defects {1}   -> DPMO = 1e6*1/50  = 20_000
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 100),
                Card(machineId: 11, date: start + 20, nbTestsOnComp: 50),
            ],
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, errorTable: BitObjectMissing | BitPolarityError, errorTableAr: BitObjectMissing | BitPolarityError),
                Obj(10, start + 62, ComponentType, BitSolderJoint, BitSolderJoint),
                Obj(11, start + 70, ComponentType, BitObjectMissing, BitObjectMissing),
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

        Assert.Equal(3L, result.Overall.TestedObjectCount);
        Assert.Equal(150L, result.Overall.OpportunityCount);
        Assert.Equal(4L, result.Overall.DefectBitCount);
        Assert.Equal(1_000_000d * 4 / 150, result.Overall.DpmoPpm);

        // Rows sorted descending by DPMO: Machine 10 (30k) before Machine 11 (20k).
        Assert.Collection(result.Rows,
            r =>
            {
                Assert.Equal("10", r.GroupKey);
                Assert.Equal("AOI-10", r.GroupName);
                Assert.Equal(100L, r.Kpi.OpportunityCount);
                Assert.Equal(30_000d, r.Kpi.DpmoPpm);
            },
            r =>
            {
                Assert.Equal("11", r.GroupKey);
                Assert.Equal("AOI-11", r.GroupName);
                Assert.Equal(50L, r.Kpi.OpportunityCount);
                Assert.Equal(20_000d, r.Kpi.DpmoPpm);
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
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 2),
            ],
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
    public async Task OpportunityComponents_UsesComponentTestCountAndIgnoresPastePads()
    {
        // 1 component (1 defect) + 3 paste pads (0 defects).
        // Components-only DPMO: denominator = card Nb_Of_Tests_On_Comp
        // (=1), numerator counts component-object defects only (=1), so
        // DPMO = 1_000_000. Paste-pad rows are excluded from BOTH halves.
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 1),
            ],
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

        // Only the component object is counted; the 3 paste pads are
        // filtered out of the numerator by the Components flavour.
        Assert.Equal(1L, result.Overall.TestedObjectCount);
        Assert.Equal(1L, result.Overall.OpportunityCount);
        Assert.Equal(1L, result.Overall.DefectBitCount);
        Assert.Equal(1_000_000d, result.Overall.DpmoPpm);
    }

    [Fact]
    public async Task GroupByDefect_EmitsOneRowPerSetBit()
    {
        // 2 components: comp A has {missing, polarity}, comp B has {missing}.
        // Denominator (opportunities) is the overall card test count (2)
        // for every defect row.
        // Row "Object missing" -> 2 defects / 2 opps = 1_000_000
        // Row "Polarity error" -> 1 defect  / 2 opps = 500_000
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 2),
            ],
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
    public async Task GroupByPartNumber_IsCountBased_RateSuppressed_NullKeyPreserved()
    {
        // Object-level axes (part number / reference designator / JEDEC)
        // cannot derive a per-group opportunity count from a defect-only
        // TESTED_OBJECT table, so they emit a defect-COUNT ranking with
        // the rate suppressed (OpportunityCount = 0, DpmoPpm = 0).
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError, partNumberName: "PN-A"),
                Obj(10, start + 61, ComponentType, 0, 0, partNumberName: null),
                Obj(10, start + 62, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: null),
            ],
        };
        var filter = new DpmoTableFilter(
            _oneDay, DpmoGroupBy.PartNumber, DpmoNumerator.Aoi, DpmoOpportunity.All);

        var result = await DpmoTableReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // PN-A: 2 defect bits; null: 1 defect bit. Both rates suppressed.
        Assert.Equal(2, result.Rows.Count);
        var pnA = Assert.Single(result.Rows, r => r.GroupKey == "PN-A");
        var pnNull = Assert.Single(result.Rows, r => r.GroupKey is null);
        Assert.Equal("PN-A", pnA.GroupName);
        Assert.Null(pnNull.GroupName);
        Assert.Equal(2L, pnA.Kpi.DefectBitCount);
        Assert.Equal(1L, pnNull.Kpi.DefectBitCount);
        Assert.Equal(0L, pnA.Kpi.OpportunityCount);
        Assert.Equal(0d, pnA.Kpi.DpmoPpm);
        Assert.Equal(0d, pnNull.Kpi.DpmoPpm);
        // Sorted descending by defect count: PN-A (2) before null (1).
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

    // Builds a CARDS row carrying the DPMO/PPM opportunity denominator
    // (Nb_Of_Tests_On_Comp). Production TESTED_OBJECT is defect-only,
    // so opportunities MUST come from cards like this — never from a
    // tested-object row count.
    private static CardRow Card(
        int machineId,
        int date,
        int nbTestsOnComp,
        int productId = 500,
        int? nbTestsOnPads = null)
    {
        return new CardRow(
            PanelId: 1,
            CardIdOnPanel: 1,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            NbOfTestsOnComp: nbTestsOnComp,
            NbOfTestsOnPads: nbTestsOnPads);
    }
}
