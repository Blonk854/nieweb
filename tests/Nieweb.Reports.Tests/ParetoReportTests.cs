using Nieweb.DataSources;
using Nieweb.Reports.TestKit;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests;

/// <summary>
/// Tests for <see cref="ParetoReport"/>. The headline scenario is the
/// "Product A vs Product B" example that motivated shipping this
/// report at all: two products where Product A has 10 defects on a
/// large volume and Product B has 5 defects on a tiny volume. A
/// DPMO-sorted view would flip B ahead of A; the volume-weighted
/// Pareto correctly keeps A on top because Product A costs the line
/// more defective boards overall.
/// </summary>
public sealed class ParetoReportTests
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

    // Bit masks matching DefectBitDecoder ordering.
    private const long BitObjectMissing = 1L << 0; // bit 1 - "Object missing"
    private const long BitPolarityError = 1L << 1; // bit 2 - "Polarity error"
    private const long BitSolderJoint = 1L << 2;   // bit 3 - "Solder joint defect"

    [Fact]
    public async Task Empty_ReturnsZeroOverallAndNoRows()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(_postReflow, result.Source);
        Assert.Equal(0L, result.Overall.OpportunityCount);
        Assert.Equal(0L, result.Overall.DefectBitCount);
        Assert.Empty(result.Rows);
        Assert.Null(result.OthersBucket);

        SnapshotAssert.Match(result, "Pareto_Empty");
    }

    /// <summary>
    /// The boss's canonical scenario, distilled to test-friendly
    /// numbers with the same mathematical shape:
    /// <list type="bullet">
    ///   <item><description>Product A — 100 opportunities, 10 defects → DPMO 100 000.</description></item>
    ///   <item><description>Product B — 20 opportunities, 5 defects → DPMO 250 000.</description></item>
    /// </list>
    /// A DPMO-ranked view would rank B first. The volume-weighted
    /// Pareto MUST rank A first because 10 defective boards hurts
    /// the line more than 5, regardless of the underlying rate.
    /// </summary>
    [Fact]
    public async Task ProductAxis_VolumeWeighted_RanksHighVolumeContributorFirst()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>(120);
        for (var i = 0; i < 100; i++)
        {
            // Product A: 10 defective components (indices 0..9) among 100.
            var hasDefect = i < 10;
            objects.Add(Obj(
                machineId: 10,
                date: start + 60 + i,
                objectId: 10_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 100));
        }
        for (var i = 0; i < 20; i++)
        {
            // Product B: 5 defective components (indices 0..4) among 20.
            var hasDefect = i < 5;
            objects.Add(Obj(
                machineId: 10,
                date: start + 60 + i,
                objectId: 20_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 200));
        }

        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(100, "Product A", null, null),
                new Product(200, "Product B", null, null),
            ],
        };

        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // Overall: 120 opportunities, 15 defects.
        Assert.Equal(120L, result.Overall.OpportunityCount);
        Assert.Equal(15L, result.Overall.DefectBitCount);

        // Two rows, both vital-few, A first even though B has higher DPMO.
        Assert.Equal(2, result.Rows.Count);
        var rowA = result.Rows[0];
        var rowB = result.Rows[1];
        Assert.Equal("Product A", rowA.GroupName);
        Assert.Equal("Product B", rowB.GroupName);
        Assert.Equal(10L, rowA.DefectCount);
        Assert.Equal(5L, rowB.DefectCount);

        // Volume decoration proves the point: B has higher DPMO but
        // still ranks below A because Pareto sorts on absolute count.
        Assert.Equal(100_000d, rowA.DpmoPpm);
        Assert.Equal(250_000d, rowB.DpmoPpm);
        Assert.True(rowB.DpmoPpm > rowA.DpmoPpm,
            "B's DPMO should be higher than A's — otherwise the test doesn't prove anything.");

        // Defect share + cumulative.
        Assert.Equal(100d * 10 / 15, rowA.DefectSharePercent);
        Assert.Equal(100d * 10 / 15, rowA.CumulativePercent);
        Assert.Equal(100d, rowB.CumulativePercent);

        // Opportunity share: A owns 100/120 of production volume.
        Assert.Equal(100d * 100 / 120, rowA.OpportunitySharePercent);

        // WeightedScore == DefectCount under ParetoWeight.Count.
        Assert.Equal(10d, rowA.WeightedScore);
        Assert.Equal(5d, rowB.WeightedScore);
    }

    [Fact]
    public async Task DefectAxis_SortsBarsByAbsoluteCountNotDpmo()
    {
        // Every opportunity is the same shape, so the DPMO-vs-count
        // distinction is: bar height = defect count per bit.
        //   Bit "Object missing" fired on 6 components
        //   Bit "Polarity error" fired on 3 components
        //   Bit "Solder joint defect" fired on 2 components
        // Denominator for every row is the overall opportunity count.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 6; i++)
        {
            objects.Add(Obj(10, start + 60 + i, 30_000 + i, ComponentType, BitObjectMissing, BitObjectMissing));
        }
        for (var i = 0; i < 3; i++)
        {
            objects.Add(Obj(10, start + 70 + i, 31_000 + i, ComponentType, BitPolarityError, BitPolarityError));
        }
        for (var i = 0; i < 2; i++)
        {
            objects.Add(Obj(10, start + 80 + i, 32_000 + i, ComponentType, BitSolderJoint, BitSolderJoint));
        }
        // 15 additional clean components (no defects) - pure denominator padding.
        for (var i = 0; i < 15; i++)
        {
            objects.Add(Obj(10, start + 90 + i, 33_000 + i, ComponentType, 0, 0));
        }

        var source = new FakeAoiSource(_postReflow) { SeededTestedObjects = objects };
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Defect);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(26L, result.Overall.OpportunityCount);
        Assert.Equal(11L, result.Overall.DefectBitCount);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("Object missing", result.Rows[0].GroupName);
        Assert.Equal(6L, result.Rows[0].DefectCount);
        Assert.Equal("Polarity error", result.Rows[1].GroupName);
        Assert.Equal(3L, result.Rows[1].DefectCount);
        Assert.Equal("Solder joint defect", result.Rows[2].GroupName);
        Assert.Equal(2L, result.Rows[2].DefectCount);

        // DPMO uses the overall opportunity count (26) as denominator for every row.
        Assert.Equal(1_000_000d * 6 / 26, result.Rows[0].DpmoPpm);
        Assert.Equal(1_000_000d * 3 / 26, result.Rows[1].DpmoPpm);
        Assert.Equal(1_000_000d * 2 / 26, result.Rows[2].DpmoPpm);

        // Cumulative %: 6/11=54.5%, +3/11 = 9/11=81.8%, +2/11 = 100%.
        Assert.Equal(100d * 6 / 11, result.Rows[0].CumulativePercent);
        Assert.Equal(100d * 9 / 11, result.Rows[1].CumulativePercent);
        Assert.Equal(100d, result.Rows[2].CumulativePercent);

        SnapshotAssert.Match(result, "Pareto_DefectAxis");
    }

    [Fact]
    public async Task VitalFewFlag_IncludesTheBarThatCrossesTheThreshold()
    {
        // 4 buckets of 5,3,1,1 defects on a Product axis
        //   totals: 10 defects, cumulative % after each: 50, 80, 90, 100
        // At the classic 80% threshold: bars 1 and 2 are vital-few
        // (the 2nd bar crosses at 80% ON THE NOSE, so it counts as
        // the last vital-few bar), bars 3 and 4 are trivial-many.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        AddProduct(objects, productId: 1, defectiveCount: 5, cleanCount: 0, start: start + 100);
        AddProduct(objects, productId: 2, defectiveCount: 3, cleanCount: 0, start: start + 200);
        AddProduct(objects, productId: 3, defectiveCount: 1, cleanCount: 0, start: start + 300);
        AddProduct(objects, productId: 4, defectiveCount: 1, cleanCount: 0, start: start + 400);

        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(1, "P1", null, null),
                new Product(2, "P2", null, null),
                new Product(3, "P3", null, null),
                new Product(4, "P4", null, null),
            ],
        };

        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Rows.Count);
        Assert.True(result.Rows[0].IsVitalFew);
        Assert.True(result.Rows[1].IsVitalFew);
        Assert.False(result.Rows[2].IsVitalFew);
        Assert.False(result.Rows[3].IsVitalFew);
    }

    [Fact]
    public async Task TopN_CollapsesOverflowIntoOthersBucket()
    {
        // 5 products with defect counts 10, 8, 6, 4, 2 (total 30).
        // TopN=3 shows P1 (10), P2 (8), P3 (6). Others = P4 (4) + P5 (2) = 6.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        AddProduct(objects, 1, 10, cleanCount: 0, start: start + 100);
        AddProduct(objects, 2, 8, cleanCount: 0, start: start + 200);
        AddProduct(objects, 3, 6, cleanCount: 0, start: start + 300);
        AddProduct(objects, 4, 4, cleanCount: 0, start: start + 400);
        AddProduct(objects, 5, 2, cleanCount: 0, start: start + 500);

        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(1, "P1", null, null),
                new Product(2, "P2", null, null),
                new Product(3, "P3", null, null),
                new Product(4, "P4", null, null),
                new Product(5, "P5", null, null),
            ],
        };

        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product, TopN: 3);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("P1", result.Rows[0].GroupName);
        Assert.Equal("P2", result.Rows[1].GroupName);
        Assert.Equal("P3", result.Rows[2].GroupName);

        Assert.NotNull(result.OthersBucket);
        Assert.Equal("Others", result.OthersBucket!.GroupName);
        Assert.Null(result.OthersBucket.GroupKey);
        Assert.Equal(6L, result.OthersBucket.DefectCount);
        Assert.Equal(100d, result.OthersBucket.CumulativePercent);
        Assert.False(result.OthersBucket.IsVitalFew);

        // Visible-row cumulative + Others share sums to 100%.
        Assert.Equal(100d * 24 / 30, result.Rows[2].CumulativePercent);
        Assert.Equal(100d * 6 / 30, result.OthersBucket.DefectSharePercent);
    }

    [Fact]
    public async Task TopN_WithoutOthersFlag_DropsOverflow()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        AddProduct(objects, 1, 5, cleanCount: 0, start: start + 100);
        AddProduct(objects, 2, 3, cleanCount: 0, start: start + 200);
        AddProduct(objects, 3, 1, cleanCount: 0, start: start + 300);

        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(1, "P1", null, null),
                new Product(2, "P2", null, null),
                new Product(3, "P3", null, null),
            ],
        };

        var filter = new ParetoFilter(
            _oneDay,
            ParetoAxis.Product,
            TopN: 2,
            IncludeOthersBucket: false);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.Null(result.OthersBucket);
    }

    [Fact]
    public async Task DrillIn_ByDefectBits_NarrowsRankingToThatDefectFamily()
    {
        // Two part numbers.
        //   PN-A: 5 "Object missing" + 1 "Polarity error"
        //   PN-B: 2 "Object missing" + 4 "Polarity error"
        // Overall Pareto by PartNumber sorts by total defects:
        //   PN-A=6, PN-B=6 -> tie broken by GroupKey (PN-A before PN-B).
        // Drill to DefectBits=[Object missing bit 1]:
        //   PN-A=5, PN-B=2 -> PN-A wins by a wider margin.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(10, start + 60 + i, 40_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-A"));
        }
        objects.Add(Obj(10, start + 65, 40_100, ComponentType, BitPolarityError, BitPolarityError, partNumberName: "PN-A"));
        for (var i = 0; i < 2; i++)
        {
            objects.Add(Obj(10, start + 70 + i, 41_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-B"));
        }
        for (var i = 0; i < 4; i++)
        {
            objects.Add(Obj(10, start + 75 + i, 41_100 + i, ComponentType, BitPolarityError, BitPolarityError, partNumberName: "PN-B"));
        }

        var source = new FakeAoiSource(_postReflow) { SeededTestedObjects = objects };

        var unfiltered = new ParetoFilter(_oneDay, ParetoAxis.PartNumber);
        var drilled = unfiltered with { DefectBits = [1] };

        var full = await ParetoReport.Instance.RunAsync(
            source, unfiltered, TestContext.Current.CancellationToken);
        var narrowed = await ParetoReport.Instance.RunAsync(
            source, drilled, TestContext.Current.CancellationToken);

        Assert.Equal(6L, full.Rows[0].DefectCount);
        Assert.Equal(6L, full.Rows[1].DefectCount);

        Assert.Equal("PN-A", narrowed.Rows[0].GroupKey);
        Assert.Equal(5L, narrowed.Rows[0].DefectCount);
        Assert.Equal("PN-B", narrowed.Rows[1].GroupKey);
        Assert.Equal(2L, narrowed.Rows[1].DefectCount);

        // The applied-filter echo lets the UI render a breadcrumb.
        Assert.Equal([1], narrowed.AppliedFilters.DefectBits);
    }

    [Fact]
    public async Task Numerator_Dummy_CountsFalseCallsOnly()
    {
        // Two components. A: Error_Table = missing+polarity, AR = missing (polarity cleared by review = 1 dummy).
        //                 B: Error_Table = solder, AR = 0 (whole thing cleared = 1 dummy).
        // Dummy count total = 2. Real = 1. AOI = 3.
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects =
            [
                Obj(10, start + 60, 50_001, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing),
                Obj(10, start + 61, 50_002, ComponentType, BitSolderJoint, 0),
            ],
            SeededProducts = [new Product(500, "Only product", null, null)],
        };

        var dummy = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product, Numerator: DpmoNumerator.Dummy),
            TestContext.Current.CancellationToken);
        var real = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product),
            TestContext.Current.CancellationToken);
        var aoi = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product, Numerator: DpmoNumerator.Aoi),
            TestContext.Current.CancellationToken);

        Assert.Equal(2L, dummy.Overall.DefectBitCount);
        Assert.Equal(1L, real.Overall.DefectBitCount);
        Assert.Equal(3L, aoi.Overall.DefectBitCount);
    }

    [Fact]
    public async Task WeightNonCount_Throws()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product, Weight: (ParetoWeight)999);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ParetoReport.Instance.RunAsync(
                source, filter, TestContext.Current.CancellationToken));
    }

    private static void AddProduct(
        List<TestedObjectRow> sink,
        int productId,
        int defectiveCount,
        int cleanCount,
        int start)
    {
        for (var i = 0; i < defectiveCount; i++)
        {
            sink.Add(Obj(
                machineId: 10,
                date: start + i,
                objectId: productId * 1_000 + i,
                objectTypeId: ComponentType,
                errorTable: BitObjectMissing,
                errorTableAr: BitObjectMissing,
                productId: productId));
        }
        for (var i = 0; i < cleanCount; i++)
        {
            sink.Add(Obj(
                machineId: 10,
                date: start + defectiveCount + i,
                objectId: productId * 1_000 + defectiveCount + i,
                objectTypeId: ComponentType,
                errorTable: 0,
                errorTableAr: 0,
                productId: productId));
        }
    }

    private static TestedObjectRow Obj(
        int machineId,
        int date,
        int objectId,
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
            ObjectId: objectId,
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
