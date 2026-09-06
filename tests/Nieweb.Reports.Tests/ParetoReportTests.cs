using Nieweb.DataSources;
using Nieweb.Filters;
using Nieweb.Reports.Common;
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
    ///   <item><description>Product A â€” 100 opportunities, 10 defects â†’ DPMO 100 000.</description></item>
    ///   <item><description>Product B â€” 20 opportunities, 5 defects â†’ DPMO 250 000.</description></item>
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
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 100, productId: 100),
                Card(machineId: 10, date: start + 20, nbTestsOnComp: 20, productId: 200),
            ],
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
            "B's DPMO should be higher than A's â€” otherwise the test doesn't prove anything.");

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

        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(machineId: 10, date: start + 10, nbTestsOnComp: 26)],
            SeededTestedObjects = objects,
        };
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
    public async Task GenericFilter_PartNumberNotLike_ExcludesMatchingRows()
    {
        // The Old-school filter builder can express operators the fixed
        // narrowing collections cannot — here "part number Not like 'PN-B'"
        // keeps only PN-A rows.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(10, start + 60 + i, 40_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-A"));
        }
        for (var i = 0; i < 3; i++)
        {
            objects.Add(Obj(10, start + 70 + i, 41_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-B"));
        }

        var source = new FakeAoiSource(_postReflow) { SeededTestedObjects = objects };

        var request = new FilterRequest(
        [
            new FilterClause(FilterField.PartNumber, FilterOperator.NotLike, ["PN-B"]),
        ]);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.PartNumber) with { Filters = request };

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("PN-A", result.Rows[0].GroupKey);
        Assert.Equal(5L, result.Rows[0].DefectCount);
    }

    [Fact]
    public async Task GenericFilter_ProductIn_ResolvesNamesAndNarrows()
    {
        // Product filter is expressed by display name; the report resolves
        // Product_Id -> name from the reference list before matching.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 4; i++)
        {
            objects.Add(Obj(10, start + 60 + i, 60_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, productId: 100));
        }
        for (var i = 0; i < 2; i++)
        {
            objects.Add(Obj(10, start + 70 + i, 61_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, productId: 200));
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

        var request = new FilterRequest(
        [
            new FilterClause(FilterField.Product, FilterOperator.In, ["Product A"]),
        ]);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product) with { Filters = request };

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("Product A", result.Rows[0].GroupName);
        Assert.Equal(4L, result.Rows[0].DefectCount);
    }

    [Fact]
    public async Task GenericFilter_DefectIn_NarrowsToNamedDefect()
    {
        // Filtering Defect In {"Polarity error"} keeps only objects that
        // carry that defect bit — proving the adapter decodes the
        // numerator bitfield to defect display names.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(10, start + 60 + i, 70_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-A"));
        }
        for (var i = 0; i < 3; i++)
        {
            objects.Add(Obj(10, start + 70 + i, 71_000 + i, ComponentType, BitPolarityError, BitPolarityError, partNumberName: "PN-A"));
        }

        var source = new FakeAoiSource(_postReflow) { SeededTestedObjects = objects };

        var request = new FilterRequest(
        [
            new FilterClause(FilterField.Defect, FilterOperator.In, ["Polarity error"]),
        ]);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.PartNumber) with { Filters = request };

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("PN-A", result.Rows[0].GroupKey);
        Assert.Equal(3L, result.Rows[0].DefectCount);
    }

    [Fact]
    public async Task ExcludeNogo_DropsProductsWhoseNameContainsNogo()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(10, start + 60 + i, 80_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, productId: 100));
        }
        for (var i = 0; i < 3; i++)
        {
            objects.Add(Obj(10, start + 70 + i, 81_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, productId: 200));
        }

        var source = new FakeAoiSource(_postReflow)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(100, "Widget-A", null, null),
                new Product(200, "nogo-cal", null, null), // case-insensitive match
            ],
        };

        var baseFilter = new ParetoFilter(_oneDay, ParetoAxis.Product);
        var withNogo = await ParetoReport.Instance.RunAsync(
            source, baseFilter, TestContext.Current.CancellationToken);
        var noNogo = await ParetoReport.Instance.RunAsync(
            source, baseFilter with { ExcludeNogo = true }, TestContext.Current.CancellationToken);

        Assert.Equal(2, withNogo.Rows.Count);
        Assert.Equal(8L, withNogo.Overall.DefectBitCount);

        Assert.Single(noNogo.Rows);
        Assert.Equal("Widget-A", noNogo.Rows[0].GroupName);
        Assert.Equal(5L, noNogo.Rows[0].DefectCount);
        Assert.Equal(5L, noNogo.Overall.DefectBitCount);
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
    public async Task Weight_UndefinedValue_Throws()
    {
        // Guards the enum boundary: RunAsync must refuse a
        // ParetoWeight value that isn't declared in the enum, not
        // silently fall through to the Count branch.
        var source = new FakeAoiSource(_postReflow);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Product, Weight: (ParetoWeight)999);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
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
        string? jedecName = null,
        int panelId = 1,
        int cardIdOnPanel = 1,
        string? repairButtonComment = null)
    {
        return new TestedObjectRow(
            PanelId: panelId,
            CardIdOnPanel: cardIdOnPanel,
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
            JedecName: jedecName,
            RepairButtonComment: repairButtonComment);
    }

    // CARDS row carrying the DPMO/PPM opportunity denominator
    // (Nb_Of_Tests_On_Comp). Opportunities come from cards, never from a
    // (defect-only) tested-object row count.
    private static CardRow Card(
        int machineId,
        int date,
        int nbTestsOnComp,
        int productId = 500,
        int panelId = 1,
        int cardIdOnPanel = 1)
        => new(
            PanelId: panelId,
            CardIdOnPanel: cardIdOnPanel,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            NbOfTestsOnComp: nbTestsOnComp);

    private static PanelRow Panel(int id, bool reviewed = true) => new(
        PanelId: id,
        MachineId: 10,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D3}",
        PanelNumericDate: (int)_oneDay.StartEpochSeconds + id,
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

    // ---------------------------------------------------------------
    // CR1: Day / Shift axes + Dpmo / Ppm weights
    // ---------------------------------------------------------------

    /// <summary>
    /// Axis=Day groups rows by local calendar day. This test spans a
    /// two-day UTC window with clearly-separated panel timestamps
    /// and asserts one row per day, sorted descending by defect
    /// count (higher-defect day ranks first).
    /// </summary>
    [Fact]
    public async Task DayAxis_BucketsRowsByCalendarDay()
    {
        var window = new DateRange(
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero));
        var day1 = (int)window.StartEpochSeconds + 3600;          // 01:00 UTC on day 1
        var day2 = (int)window.StartEpochSeconds + 86400 + 3600;  // 01:00 UTC on day 2

        var objects = new List<TestedObjectRow>();
        // Day 1: 5 defective + 5 clean
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(machineId: 1, date: day1 + i, objectId: 1_000 + i,
                objectTypeId: ComponentType, errorTable: BitObjectMissing, errorTableAr: BitObjectMissing));
        }
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(machineId: 1, date: day1 + 10 + i, objectId: 2_000 + i,
                objectTypeId: ComponentType, errorTable: 0, errorTableAr: 0));
        }
        // Day 2: 2 defective + 8 clean
        for (var i = 0; i < 2; i++)
        {
            objects.Add(Obj(machineId: 1, date: day2 + i, objectId: 3_000 + i,
                objectTypeId: ComponentType, errorTable: BitObjectMissing, errorTableAr: BitObjectMissing));
        }
        for (var i = 0; i < 8; i++)
        {
            objects.Add(Obj(machineId: 1, date: day2 + 10 + i, objectId: 4_000 + i,
                objectTypeId: ComponentType, errorTable: 0, errorTableAr: 0));
        }

        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 1, date: day1 + 5, nbTestsOnComp: 100),
                Card(machineId: 1, date: day2 + 5, nbTestsOnComp: 100),
            ],
            SeededTestedObjects = objects,
        };
        var filter = new ParetoFilter(
            window,
            ParetoAxis.Day,
            SiteTimeZone: TimeZoneInfo.Utc);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(5L, result.Rows[0].DefectCount);
        Assert.Equal("2026-03-01", result.Rows[0].GroupKey);
        Assert.Equal(2L, result.Rows[1].DefectCount);
        Assert.Equal("2026-03-02", result.Rows[1].GroupKey);
        SnapshotAssert.Match(result, "Pareto_DayAxis");
    }

    /// <summary>
    /// Axis=Shift buckets rows into a 3-shift schedule (08:00,
    /// 16:00, 00:00 UTC). Rows land in the shift whose half-open
    /// window contains their timestamp.
    /// </summary>
    [Fact]
    public async Task ShiftAxis_BucketsRowsByShiftDefinition()
    {
        var window = new DateRange(
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero));

        // UTC epoch for 2026-03-01T00:00Z:
        var baseEpoch = (int)window.StartEpochSeconds;
        var shift1Time = baseEpoch + (10 * 3600); // 10:00Z â†’ Shift starting 08:00
        var shift2Time = baseEpoch + (18 * 3600); // 18:00Z â†’ Shift starting 16:00
        var shift3Time = baseEpoch + (2 * 3600);  // 02:00Z â†’ Shift starting 00:00

        var objects = new List<TestedObjectRow>
        {
            Obj(1, shift1Time, 1, ComponentType, BitObjectMissing, BitObjectMissing),
            Obj(1, shift1Time + 60, 2, ComponentType, BitObjectMissing, BitObjectMissing),
            Obj(1, shift2Time, 3, ComponentType, BitObjectMissing, BitObjectMissing),
            Obj(1, shift3Time, 4, ComponentType, 0, 0),
        };
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 1, date: shift1Time, nbTestsOnComp: 100),
                Card(machineId: 1, date: shift2Time, nbTestsOnComp: 100),
                Card(machineId: 1, date: shift3Time, nbTestsOnComp: 100),
            ],
            SeededTestedObjects = objects,
        };
        var shifts = ShiftDefinition.FromStarts(
            new[] { new TimeOnly(8, 0), new TimeOnly(16, 0), new TimeOnly(0, 0) });

        var filter = new ParetoFilter(
            window,
            ParetoAxis.Shift,
            SiteTimeZone: TimeZoneInfo.Utc,
            Shifts: shifts);

        var result = await ParetoReport.Instance.RunAsync(
            source, filter, TestContext.Current.CancellationToken);

        // Two shifts carry defects and rank as bars; the 00:00 shift
        // has only a clean opportunity (no defect), so it is not a
        // Pareto contributor and does not appear as a bar.
        Assert.Contains(result.Rows, r => r.DefectCount == 2);
        Assert.Contains(result.Rows, r => r.DefectCount == 1);
        SnapshotAssert.Match(result, "Pareto_ShiftAxis");
    }

    /// <summary>
    /// Axis=Shift with no ShiftDefinition is a client error.
    /// </summary>
    [Fact]
    public async Task ShiftAxis_WithoutShiftDefinition_Throws()
    {
        var source = new FakeAoiSource(_postReflow);
        var filter = new ParetoFilter(_oneDay, ParetoAxis.Shift);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ParetoReport.Instance.RunAsync(
                source, filter, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Weight=Dpmo flips the ranking compared to Weight=Count. Using
    /// the boss-approved A vs B scenario: A has 10 defects on 100
    /// opportunities (DPMO 100 000); B has 5 defects on 20
    /// opportunities (DPMO 250 000). Volume ranks A first, DPMO
    /// ranks B first.
    /// </summary>
    [Fact]
    public async Task Weight_Dpmo_ReversesVolumeRanking()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        // Product A: 100 opps, 10 defects
        for (var i = 0; i < 100; i++)
        {
            var hasDefect = i < 10;
            objects.Add(Obj(
                machineId: 10, date: start + 60 + i, objectId: 10_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 100));
        }
        // Product B: 20 opps, 5 defects
        for (var i = 0; i < 20; i++)
        {
            var hasDefect = i < 5;
            objects.Add(Obj(
                machineId: 10, date: start + 200 + i, objectId: 20_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 200));
        }
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 100, productId: 100),
                Card(machineId: 10, date: start + 20, nbTestsOnComp: 20, productId: 200),
            ],
            SeededTestedObjects = objects,
        };

        var byCount = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product, Weight: ParetoWeight.Count),
            TestContext.Current.CancellationToken);
        var byDpmo = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product, Weight: ParetoWeight.Dpmo),
            TestContext.Current.CancellationToken);

        // Count view: Product A first.
        Assert.Equal("100", byCount.Rows[0].GroupKey);
        Assert.Equal(10, byCount.Rows[0].WeightedScore);
        // Dpmo view: Product B first with score 250 000; A second with 100 000.
        Assert.Equal("200", byDpmo.Rows[0].GroupKey);
        Assert.Equal(250_000d, byDpmo.Rows[0].WeightedScore);
        Assert.Equal("100", byDpmo.Rows[1].GroupKey);
        Assert.Equal(100_000d, byDpmo.Rows[1].WeightedScore);
        SnapshotAssert.Match(byDpmo, "Pareto_Weight_Dpmo");
    }

    /// <summary>
    /// Weight=Ppm is a display alias for Weight=Dpmo â€” the numeric
    /// output must be byte-identical.
    /// </summary>
    [Fact]
    public async Task Weight_Ppm_ProducesSameNumbersAsDpmo()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 100; i++)
        {
            var hasDefect = i < 10;
            objects.Add(Obj(
                machineId: 10, date: start + 60 + i, objectId: 10_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 100));
        }
        for (var i = 0; i < 20; i++)
        {
            var hasDefect = i < 5;
            objects.Add(Obj(
                machineId: 10, date: start + 200 + i, objectId: 20_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 200));
        }
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(machineId: 10, date: start + 10, nbTestsOnComp: 100, productId: 100),
                Card(machineId: 10, date: start + 20, nbTestsOnComp: 20, productId: 200),
            ],
            SeededTestedObjects = objects,
        };

        var byDpmo = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product, Weight: ParetoWeight.Dpmo),
            TestContext.Current.CancellationToken);
        var byPpm = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product, Weight: ParetoWeight.Ppm),
            TestContext.Current.CancellationToken);

        Assert.Equal(byDpmo.Rows.Count, byPpm.Rows.Count);
        for (var i = 0; i < byDpmo.Rows.Count; i++)
        {
            Assert.Equal(byDpmo.Rows[i].GroupKey, byPpm.Rows[i].GroupKey);
            Assert.Equal(byDpmo.Rows[i].WeightedScore, byPpm.Rows[i].WeightedScore);
            Assert.Equal(byDpmo.Rows[i].DpmoPpm, byPpm.Rows[i].DpmoPpm);
        }
    }

    /// Object-level axes have no per-group card denominator. Subpanel is
    /// card-derivable and must not be added to this theory.
    [Theory]
    [InlineData(ParetoAxis.ReferenceDesignator)]
    [InlineData(ParetoAxis.PartNumber)]
    [InlineData(ParetoAxis.Jedec)]
    public async Task ObjectLevelAxes_OpportunitiesNotApplicable(ParetoAxis axis)
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 50)],
            SeededTestedObjects =
            [
                Obj(10, start + 60, 1, ComponentType, BitObjectMissing, BitObjectMissing,
                    topology: "U1", partNumberName: "PN-A", jedecName: "SOIC8"),
            ],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source, new ParetoFilter(_oneDay, axis), TestContext.Current.CancellationToken);

        Assert.True(result.Overall.OpportunityCount > 0);
        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, r =>
        {
            Assert.False(r.OpportunitiesApplicable);
            Assert.Equal(0L, r.OpportunityCount);
            Assert.Equal(0d, r.DpmoPpm);
        });
        Assert.Equal(ParetoWeight.Count, result.Weight);
        Assert.Equal(80.0, result.VitalFewThresholdPercent);
    }

    [Fact]
    public async Task ObjectLevel_RequestedDpmo_EchoesCountAndRanksByDefects()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 50)],
            SeededTestedObjects =
            [
                Obj(10, start + 60, 1, ComponentType, BitObjectMissing, BitObjectMissing,
                    partNumberName: "PN-A"),
                Obj(10, start + 61, 2, ComponentType, BitObjectMissing, BitObjectMissing,
                    partNumberName: "PN-A"),
                Obj(10, start + 62, 3, ComponentType, BitObjectMissing, BitObjectMissing,
                    partNumberName: "PN-B"),
            ],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.PartNumber, Weight: ParetoWeight.Dpmo),
            TestContext.Current.CancellationToken);

        Assert.Equal(ParetoWeight.Count, result.Weight);
        Assert.Equal("PN-A", result.Rows[0].GroupKey);
        Assert.Equal(2L, result.Rows[0].DefectCount);
        Assert.Equal(2d, result.Rows[0].WeightedScore);
        Assert.False(result.Rows[0].OpportunitiesApplicable);
    }

    [Fact]
    public async Task ProductAxis_TrueZeroOpportunities_StillApplicable()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 0, productId: 100)],
            SeededTestedObjects =
            [
                Obj(10, start + 60, 1, ComponentType, BitObjectMissing, BitObjectMissing, productId: 100),
            ],
            SeededProducts = [new Product(100, "P-zero", null, null)],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source, new ParetoFilter(_oneDay, ParetoAxis.Product), TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.True(result.Rows[0].OpportunitiesApplicable);
        Assert.Equal(0L, result.Rows[0].OpportunityCount);
        Assert.Equal(0d, result.Rows[0].DpmoPpm);
        Assert.Equal(1L, result.Rows[0].DefectCount);
    }

    [Fact]
    public async Task MultiBitObject_CountsOccurrencesOnProductAndDefectAxes()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var twoBits = BitObjectMissing | BitPolarityError;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 10, productId: 100)],
            SeededTestedObjects =
            [
                Obj(10, start + 60, 1, ComponentType, twoBits, twoBits, productId: 100),
            ],
            SeededProducts = [new Product(100, "P1", null, null)],
        };

        var product = await ParetoReport.Instance.RunAsync(
            source, new ParetoFilter(_oneDay, ParetoAxis.Product), TestContext.Current.CancellationToken);
        Assert.Equal(1L, product.Overall.TestedObjectCount);
        Assert.Equal(2L, product.Overall.DefectBitCount);
        Assert.Equal(2L, product.Rows[0].DefectCount);

        var defect = await ParetoReport.Instance.RunAsync(
            source, new ParetoFilter(_oneDay, ParetoAxis.Defect), TestContext.Current.CancellationToken);
        Assert.Equal(2, defect.Rows.Count);
        Assert.All(defect.Rows, r => Assert.Equal(1L, r.DefectCount));
        Assert.All(defect.Rows, r => Assert.True(r.OpportunitiesApplicable));
    }

    [Fact]
    public async Task DefectTopN_OthersUsesOverallDenominatorOnce()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>();
        void AddBit(long bit, int count, int idBase)
        {
            for (var i = 0; i < count; i++)
            {
                objects.Add(Obj(10, start + idBase + i, idBase + i, ComponentType, bit, bit));
            }
        }
        AddBit(BitObjectMissing, 8, 1000);
        AddBit(BitPolarityError, 6, 2000);
        AddBit(BitSolderJoint, 4, 3000);
        var bit4 = 1L << 3;
        AddBit(bit4, 2, 4000);

        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 40)],
            SeededTestedObjects = objects,
        };

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Defect, TopN: 2),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.NotNull(result.OthersBucket);
        Assert.True(result.OthersBucket!.OpportunitiesApplicable);
        Assert.Equal(result.Overall.OpportunityCount, result.OthersBucket.OpportunityCount);
        Assert.Equal(100d, result.OthersBucket.OpportunitySharePercent);
        Assert.Equal(
            1_000_000d * result.OthersBucket.DefectCount / result.Overall.OpportunityCount,
            result.OthersBucket.DpmoPpm);

        var byDpmo = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Defect, Weight: ParetoWeight.Dpmo, TopN: 2),
            TestContext.Current.CancellationToken);
        Assert.Equal(byDpmo.OthersBucket!.DpmoPpm, byDpmo.OthersBucket.WeightedScore);
        Assert.Equal(result.Overall.OpportunityCount, byDpmo.OthersBucket.OpportunityCount);
    }

    // ---------------------------------------------------------------
    // Subpanel axis (Card_Number / CardIdOnPanel)
    // ---------------------------------------------------------------

    [Fact]
    public async Task SubpanelAxis_DifferentSlotsProduceDifferentBars()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = TwoSlotSource(
            start,
            slot1Defects: 3,
            slot1Opps: 50,
            slot2Defects: 1,
            slot2Opps: 50);

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("1", result.Rows[0].GroupKey);
        Assert.Equal("1", result.Rows[0].GroupName);
        Assert.Equal(3L, result.Rows[0].DefectCount);
        Assert.Equal("2", result.Rows[1].GroupKey);
        Assert.Equal("2", result.Rows[1].GroupName);
        Assert.Equal(1L, result.Rows[1].DefectCount);
        Assert.Equal(4L, result.Overall.DefectBitCount);
        Assert.Equal(100L, result.Overall.OpportunityCount);
    }

    [Fact]
    public async Task SubpanelAxis_SameSlotAcrossPanelsCombines()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, start + 10, nbTestsOnComp: 40, panelId: 10, cardIdOnPanel: 1),
                Card(10, start + 20, nbTestsOnComp: 60, panelId: 11, cardIdOnPanel: 1),
            ],
            SeededTestedObjects =
            [
                ..DefectsOnSlot(start, panelId: 10, cardIdOnPanel: 1, count: 3, idBase: 100),
                ..DefectsOnSlot(start, panelId: 11, cardIdOnPanel: 1, count: 2, idBase: 200),
            ],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0].GroupKey);
        Assert.Equal("1", result.Rows[0].GroupName);
        Assert.Equal(5L, result.Rows[0].DefectCount);
        Assert.Equal(100L, result.Rows[0].OpportunityCount);
        Assert.Equal(100L, result.Overall.OpportunityCount);
        Assert.Equal(100d, result.Rows[0].OpportunitySharePercent);
    }

    [Fact]
    public async Task SubpanelAxis_UsesPerSlotOpportunityDenominator()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = TwoSlotSource(
            start,
            slot1Defects: 2,
            slot1Opps: 80,
            slot2Defects: 2,
            slot2Opps: 20);

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel),
            TestContext.Current.CancellationToken);

        Assert.Equal(ParetoWeight.Count, result.Weight);
        Assert.All(result.Rows, r => Assert.True(r.OpportunitiesApplicable));
        Assert.Equal(100L, result.Overall.OpportunityCount);
        var slot1 = result.Rows.Single(r => r.GroupKey == "1");
        var slot2 = result.Rows.Single(r => r.GroupKey == "2");
        Assert.Equal(80L, slot1.OpportunityCount);
        Assert.Equal(20L, slot2.OpportunityCount);
        Assert.Equal(80d, slot1.OpportunitySharePercent);
        Assert.Equal(20d, slot2.OpportunitySharePercent);
        Assert.Equal(25_000d, slot1.DpmoPpm);
        Assert.Equal(100_000d, slot2.DpmoPpm);
    }

    [Fact]
    public async Task SubpanelAxis_DpmoCanReorderCountRanking()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = TwoSlotSource(
            start,
            slot1Defects: 10,
            slot1Opps: 100,
            slot2Defects: 5,
            slot2Opps: 10);

        var byCount = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Weight: ParetoWeight.Count),
            TestContext.Current.CancellationToken);
        var byDpmo = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Weight: ParetoWeight.Dpmo),
            TestContext.Current.CancellationToken);

        Assert.Equal("1", byCount.Rows[0].GroupKey);
        Assert.Equal(10L, byCount.Rows[0].DefectCount);
        Assert.Equal(ParetoWeight.Dpmo, byDpmo.Weight);
        Assert.Equal("2", byDpmo.Rows[0].GroupKey);
        Assert.Equal(500_000d, byDpmo.Rows[0].WeightedScore);
        Assert.Equal(byDpmo.Rows[0].DpmoPpm, byDpmo.Rows[0].WeightedScore);
    }

    [Fact]
    public async Task SubpanelAxis_PpmUsesSameRateAsDpmo()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = TwoSlotSource(
            start,
            slot1Defects: 10,
            slot1Opps: 100,
            slot2Defects: 5,
            slot2Opps: 10);

        var byDpmo = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Weight: ParetoWeight.Dpmo),
            TestContext.Current.CancellationToken);
        var byPpm = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Weight: ParetoWeight.Ppm),
            TestContext.Current.CancellationToken);

        Assert.Equal(ParetoWeight.Ppm, byPpm.Weight);
        Assert.Equal(byDpmo.Rows.Count, byPpm.Rows.Count);
        for (var i = 0; i < byDpmo.Rows.Count; i++)
        {
            Assert.Equal(byDpmo.Rows[i].GroupKey, byPpm.Rows[i].GroupKey);
            Assert.Equal(byDpmo.Rows[i].WeightedScore, byPpm.Rows[i].WeightedScore);
            Assert.Equal(byDpmo.Rows[i].DpmoPpm, byPpm.Rows[i].DpmoPpm);
        }
    }

    [Fact]
    public async Task CardNumbersFilter_ScopesBothNumeratorAndDenominator()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = TwoSlotSource(
            start,
            slot1Defects: 2,
            slot1Opps: 10,
            slot2Defects: 8,
            slot2Opps: 40);

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Product) { CardNumbers = [1] },
            TestContext.Current.CancellationToken);

        Assert.Equal([1], result.AppliedFilters.CardNumbers);
        Assert.Equal(10L, result.Overall.OpportunityCount);
        Assert.Equal(2L, result.Overall.DefectBitCount);
        Assert.Equal(2L, result.Overall.TestedObjectCount);
        Assert.Single(result.Rows);
        Assert.Equal(2L, result.Rows[0].DefectCount);
        Assert.Equal(10L, result.Rows[0].OpportunityCount);
    }

    [Fact]
    public async Task CardNumbersFilter_AndsWithTopologyPartNumberAndDefectBits()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, start + 10, nbTestsOnComp: 50, cardIdOnPanel: 1),
                Card(10, start + 11, nbTestsOnComp: 50, cardIdOnPanel: 2),
            ],
            SeededTestedObjects =
            [
                Obj(10, start + 60, 1, ComponentType, BitObjectMissing, BitObjectMissing,
                    topology: "R12", partNumberName: "PN-A", cardIdOnPanel: 1),
                Obj(10, start + 61, 2, ComponentType, BitObjectMissing, BitObjectMissing,
                    topology: "R12", partNumberName: "PN-B", cardIdOnPanel: 1),
                Obj(10, start + 62, 3, ComponentType, BitObjectMissing, BitObjectMissing,
                    topology: "R12", partNumberName: "PN-A", cardIdOnPanel: 2),
                Obj(10, start + 63, 4, ComponentType, BitPolarityError, BitPolarityError,
                    topology: "R12", partNumberName: "PN-A", cardIdOnPanel: 1),
            ],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel)
            {
                CardNumbers = [1],
                Topologies = ["R12"],
                PartNumbers = ["PN-A"],
                DefectBits = [1],
            },
            TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0].GroupKey);
        Assert.Equal(1L, result.Rows[0].DefectCount);
        Assert.Equal(1L, result.Overall.DefectBitCount);
        Assert.Equal(50L, result.Overall.OpportunityCount);
        Assert.Equal(["R12"], result.AppliedFilters.Topologies);
        Assert.Equal(["PN-A"], result.AppliedFilters.PartNumbers);
        Assert.Equal([1], result.AppliedFilters.DefectBits);
        Assert.Equal([1], result.AppliedFilters.CardNumbers);
    }

    [Fact]
    public async Task CardNumbersFilter_ScopesSkipExcludedCount()
    {
        var source = BuildSkipPairSource();

        var allSlots = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Numerator: DpmoNumerator.Aoi)
            {
                SkipExclusion = SkipExclusion.Clean,
            },
            TestContext.Current.CancellationToken);
        var skippedSlot = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Numerator: DpmoNumerator.Aoi)
            {
                SkipExclusion = SkipExclusion.Clean,
                CardNumbers = [2],
            },
            TestContext.Current.CancellationToken);
        var keptSlot = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Numerator: DpmoNumerator.Aoi)
            {
                SkipExclusion = SkipExclusion.Clean,
                CardNumbers = [1],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1L, allSlots.SkipExcludedCards);
        Assert.Equal(1L, allSlots.Overall.DefectBitCount);
        Assert.Equal(100L, allSlots.Overall.OpportunityCount);

        Assert.Equal(1L, skippedSlot.SkipExcludedCards);
        Assert.Equal(0L, skippedSlot.Overall.DefectBitCount);
        Assert.Equal(0L, skippedSlot.Overall.OpportunityCount);

        Assert.Equal(0L, keptSlot.SkipExcludedCards);
        Assert.Equal(1L, keptSlot.Overall.DefectBitCount);
        Assert.Equal(100L, keptSlot.Overall.OpportunityCount);
    }

    [Fact]
    public async Task SubpanelAxis_SkipExclusionDropsCardFromBothPasses()
    {
        var source = BuildSkipPairSource();

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, Numerator: DpmoNumerator.Aoi)
            {
                SkipExclusion = SkipExclusion.Clean,
            },
            TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0].GroupKey);
        Assert.Equal(1L, result.Rows[0].DefectCount);
        Assert.Equal(100L, result.Rows[0].OpportunityCount);
        Assert.Equal(100L, result.Overall.OpportunityCount);
        Assert.Equal(1L, result.SkipExcludedCards);
    }

    [Fact]
    public async Task SubpanelAxis_ZeroDefectSlotsAreOmitted()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = TwoSlotSource(
            start,
            slot1Defects: 3,
            slot1Opps: 40,
            slot2Defects: 0,
            slot2Opps: 40);

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0].GroupKey);
        Assert.Equal(80L, result.Overall.OpportunityCount);
        Assert.Equal(40L, result.Rows[0].OpportunityCount);
        Assert.Equal(50d, result.Rows[0].OpportunitySharePercent);
    }

    [Fact]
    public async Task SubpanelAxis_TopNOthersSumsHiddenSlotOpportunities()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards =
            [
                Card(10, start + 10, nbTestsOnComp: 100, cardIdOnPanel: 1),
                Card(10, start + 11, nbTestsOnComp: 40, cardIdOnPanel: 2),
                Card(10, start + 12, nbTestsOnComp: 10, cardIdOnPanel: 3),
            ],
            SeededTestedObjects =
            [
                ..DefectsOnSlot(start, panelId: 1, cardIdOnPanel: 1, count: 8, idBase: 100),
                ..DefectsOnSlot(start, panelId: 1, cardIdOnPanel: 2, count: 4, idBase: 200),
                ..DefectsOnSlot(start, panelId: 1, cardIdOnPanel: 3, count: 2, idBase: 300),
            ],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel, TopN: 1),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0].GroupKey);
        Assert.NotNull(result.OthersBucket);
        Assert.True(result.OthersBucket!.OpportunitiesApplicable);
        Assert.Equal(6L, result.OthersBucket.DefectCount);
        Assert.Equal(50L, result.OthersBucket.OpportunityCount);
        Assert.NotEqual(result.Overall.OpportunityCount, result.OthersBucket.OpportunityCount);
        Assert.Equal(150L, result.Overall.OpportunityCount);
        Assert.Equal(
            1_000_000d * 6 / 50,
            result.OthersBucket.DpmoPpm);
    }

    [Fact]
    public async Task SubpanelAxis_ZeroSlotIsAValidGroup()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = new FakeAoiSource(_postReflow)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 25, cardIdOnPanel: 0)],
            SeededTestedObjects = [..DefectsOnSlot(start, panelId: 1, cardIdOnPanel: 0, count: 2, idBase: 10)],
        };

        var result = await ParetoReport.Instance.RunAsync(
            source,
            new ParetoFilter(_oneDay, ParetoAxis.Subpanel),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal("0", result.Rows[0].GroupKey);
        Assert.Equal("0", result.Rows[0].GroupName);
        Assert.Equal(2L, result.Rows[0].DefectCount);
        Assert.Equal(25L, result.Rows[0].OpportunityCount);
        Assert.True(result.Rows[0].OpportunitiesApplicable);
    }

    private static FakeAoiSource TwoSlotSource(
        int start,
        int slot1Defects,
        int slot1Opps,
        int slot2Defects,
        int slot2Opps)
        => new(_postReflow)
        {
            SeededCards =
            [
                Card(10, start + 10, nbTestsOnComp: slot1Opps, cardIdOnPanel: 1),
                Card(10, start + 11, nbTestsOnComp: slot2Opps, cardIdOnPanel: 2),
            ],
            SeededTestedObjects =
            [
                ..DefectsOnSlot(start, panelId: 1, cardIdOnPanel: 1, count: slot1Defects, idBase: 100),
                ..DefectsOnSlot(start, panelId: 1, cardIdOnPanel: 2, count: slot2Defects, idBase: 200),
            ],
        };

    private static List<TestedObjectRow> DefectsOnSlot(
        int start, int panelId, int cardIdOnPanel, int count, int idBase)
    {
        var rows = new List<TestedObjectRow>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(Obj(
                machineId: 10,
                date: start + 60 + i,
                objectId: idBase + i,
                objectTypeId: ComponentType,
                errorTable: BitObjectMissing,
                errorTableAr: BitObjectMissing,
                panelId: panelId,
                cardIdOnPanel: cardIdOnPanel));
        }
        return rows;
    }

    private static FakeAoiSource BuildSkipPairSource()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var tos = new List<TestedObjectRow>
        {
            Obj(10, start + 60, 1, ComponentType, BitObjectMissing, BitObjectMissing,
                panelId: 1, cardIdOnPanel: 1),
        };
        for (var i = 0; i < 50; i++)
        {
            tos.Add(Obj(
                10, start + 70 + i, 100 + i, ComponentType, BitObjectMissing, BitObjectMissing,
                panelId: 1, cardIdOnPanel: 2,
                repairButtonComment: i == 0 ? "X-OUT" : null));
        }

        return new FakeAoiSource(_postReflow)
        {
            SeededPanels = [Panel(1, reviewed: true)],
            SeededCards =
            [
                Card(10, start + 10, nbTestsOnComp: 100, panelId: 1, cardIdOnPanel: 1),
                Card(10, start + 11, nbTestsOnComp: 100, panelId: 1, cardIdOnPanel: 2),
            ],
            SeededTestedObjects = tos,
        };
    }
}

