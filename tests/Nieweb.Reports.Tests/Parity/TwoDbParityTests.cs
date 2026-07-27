using Nieweb.DataSources;
using Nieweb.Reports.Tests.Fakes;
using Xunit;

namespace Nieweb.Reports.Tests.Parity;

/// <summary>
/// T3 — two-DB parity fixtures. Every Nieweb report must produce
/// numerically identical output for identical seeded rows regardless
/// of which live Superviseur database the input came from
/// (<c>HLYAOI2024</c> post-reflow schema 5.0 or <c>MEAOI</c> pre-reflow
/// schema 4.3.1). The only difference on the wire is the embedded
/// <see cref="SourceDescriptor"/> — every KPI, row order, and count
/// must match to the last significant digit.
/// </summary>
/// <remarks>
/// <para>
/// The report layer currently does not consult <see cref="SourceDescriptor.Caps"/>
/// when aggregating (verified 2026-07-21 with a workspace-wide search),
/// so this suite is also a change-guard: if a future refactor branches
/// on <c>source.Descriptor.Caps</c> inside <see cref="FpyTableReport"/>,
/// <see cref="DpmoTableReport"/>, <see cref="ParetoReport"/>, or
/// <see cref="PanelYieldByLineReport"/>, the parity assertions here
/// will start diverging and force an explicit review.
/// </para>
/// <para>
/// The suite also documents the two <em>schema-driven</em> deltas
/// consumers must be aware of:
/// </para>
/// <list type="number">
///   <item>
///     Pre-reflow (v4.3.1) can emit <c>Panel_Status = 3</c> ("good
///     after review"); post-reflow (v5.0) never does. Both classify it
///     as "good" for FPY-after-repair.
///   </item>
///   <item>
///     Pre-reflow <c>TESTED_OBJECT</c> lacks the <c>ERROR_TABLE_AR</c>
///     column, so <see cref="TestedObjectRow.ErrorTableAr"/> is always
///     <c>0</c>. Consumers requesting <see cref="DpmoNumerator.Real"/>
///     against pre-reflow will therefore always see zero real defects.
///   </item>
/// </list>
/// </remarks>
public sealed class TwoDbParityTests
{
    private static readonly DateRange _oneDay = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    private const int ComponentType = 0x01;
    private const long BitObjectMissing = 1L << 0;
    private const long BitPolarityError = 1L << 1;
    private const long BitSolderJoint = 1L << 2;

    /// <summary>
    /// xUnit theory data yielding the pair of live descriptors. Kept
    /// as a member method (not a data attribute) so future descriptors
    /// (e.g. a Sigmalink SPI source) can be added without touching the
    /// call sites.
    /// </summary>
    public static TheoryData<SourceDescriptor> BothDescriptors() =>
        new()
        {
            ParityDescriptors.PostReflow,
            ParityDescriptors.PreReflow,
        };

    // -------------------------------------------------------------------------
    // Sanity: descriptors are structurally different but the reports do
    // not read anything off Caps.
    // -------------------------------------------------------------------------

    [Fact]
    public void PostAndPreReflowDescriptors_HaveDisjointCapabilityBitsets()
    {
        // If this ever fails, either the fixtures drifted from the real
        // sources or the two DBs really did converge — update
        // ParityDescriptors + this assertion in the same commit.
        Assert.NotEqual(ParityDescriptors.PostReflow.Caps, ParityDescriptors.PreReflow.Caps);
        Assert.Equal("5.0", ParityDescriptors.PostReflow.SchemaVersion);
        Assert.Equal("4.3.1", ParityDescriptors.PreReflow.SchemaVersion);
    }

    // -------------------------------------------------------------------------
    // Parity: PanelYieldByLine
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PanelYield_IdenticalPanels_ProduceIdenticalKpisAcrossBothDbs()
    {
        var panels = SeedYieldPanels();
        var machines = SeedMachines();

        var post = await PanelYieldByLineReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PostReflow, panels, machines),
            new PanelYieldFilter(_oneDay),
            TestContext.Current.CancellationToken);
        var pre = await PanelYieldByLineReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PreReflow, panels, machines),
            new PanelYieldFilter(_oneDay),
            TestContext.Current.CancellationToken);

        // Every KPI + row-set must match; only the embedded Source differs.
        Assert.Equal(post.Overall, pre.Overall);
        Assert.Equal(post.ByMachine, pre.ByMachine);
        Assert.Equal(post.Window, pre.Window);
        Assert.Equal(ParityDescriptors.PostReflow, post.Source);
        Assert.Equal(ParityDescriptors.PreReflow, pre.Source);
    }

    // -------------------------------------------------------------------------
    // Parity: FpyTable
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothDescriptors))]
    public async Task FpyTable_Panel_ByMachine_ProducesSameKpisAcrossDescriptors(SourceDescriptor descriptor)
    {
        // Compare each descriptor's result against a fixed golden
        // result computed by hand from the seed. Because the report
        // is descriptor-agnostic, both runs must land on the same
        // numbers.
        var panels = SeedYieldPanels(); // status set is {1, 1, 2, -1, 0}
        var machines = SeedMachines();
        var source = NewSource(descriptor, panels, machines);

        var result = await FpyTableReport.Instance.RunAsync(
            source,
            new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine),
            TestContext.Current.CancellationToken);

        // 5 panels total across machines 10 + 11.
        Assert.Equal(5, result.Overall.TotalRows);
        Assert.Equal(4, result.Overall.InspectedCount);
        Assert.Equal(1, result.Overall.NotInspectedCount);
        Assert.Equal(1, result.Overall.FaultyCount);
        Assert.Equal(2, result.Overall.GoodAoiCount);
        Assert.Equal(3, result.Overall.GoodDiagnosticCount);
        Assert.Equal(3, result.Overall.GoodAfterRepairCount);
        Assert.Equal(descriptor, result.Source);
    }

    [Fact]
    public async Task FpyTable_IdenticalPanels_ProduceIdenticalKpisAcrossBothDbs()
    {
        var panels = SeedYieldPanels();
        var machines = SeedMachines();

        var post = await FpyTableReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PostReflow, panels, machines),
            new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine),
            TestContext.Current.CancellationToken);
        var pre = await FpyTableReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PreReflow, panels, machines),
            new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine),
            TestContext.Current.CancellationToken);

        Assert.Equal(post.Overall, pre.Overall);
        Assert.Equal(post.Rows, pre.Rows);
    }

    // -------------------------------------------------------------------------
    // Parity: DpmoTable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DpmoTable_IdenticalObjects_ProduceIdenticalKpisAcrossBothDbs()
    {
        var objects = SeedDefectObjects();
        var machines = SeedMachines();

        var post = await DpmoTableReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PostReflow, objects: objects, machines: machines),
            new DpmoTableFilter(_oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.All),
            TestContext.Current.CancellationToken);
        var pre = await DpmoTableReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PreReflow, objects: objects, machines: machines),
            new DpmoTableFilter(_oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Aoi, DpmoOpportunity.All),
            TestContext.Current.CancellationToken);

        Assert.Equal(post.Overall, pre.Overall);
        Assert.Equal(post.Rows, pre.Rows);
        Assert.NotEqual(post.Source, pre.Source);
    }

    // -------------------------------------------------------------------------
    // Parity: Pareto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Pareto_IdenticalObjects_ProduceIdenticalKpisAcrossBothDbs()
    {
        var objects = SeedDefectObjects();
        var machines = SeedMachines();

        var post = await ParetoReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PostReflow, objects: objects, machines: machines),
            new ParetoFilter(_oneDay, ParetoAxis.Defect),
            TestContext.Current.CancellationToken);
        var pre = await ParetoReport.Instance.RunAsync(
            NewSource(ParityDescriptors.PreReflow, objects: objects, machines: machines),
            new ParetoFilter(_oneDay, ParetoAxis.Defect),
            TestContext.Current.CancellationToken);

        Assert.Equal(post.Overall, pre.Overall);
        Assert.Equal(post.Rows, pre.Rows);
        Assert.Equal(post.OthersBucket, pre.OthersBucket);
    }

    // -------------------------------------------------------------------------
    // Documented deltas: schema-driven divergences.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pre-reflow schema 4.3.1 lets <c>Panel_Status = 3</c> appear.
    /// Both DBs classify it consistently — the sanity check is that
    /// running the same status-3 panel through either descriptor gives
    /// the same "good after repair" bump.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothDescriptors))]
    public async Task FpyTable_PanelStatus3_ClassifiesConsistentlyAcrossDbs(SourceDescriptor descriptor)
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var source = NewSource(
            descriptor,
            panels:
            [
                Panel(1, machineId: 10, date: start + 60, status: 1),   // GoodAoi
                Panel(2, machineId: 10, date: start + 120, status: 3),  // GoodAr only
                Panel(3, machineId: 10, date: start + 180, status: -1), // Faulty
            ],
            machines: [new Machine(10, 2, "AOI-10", "AOI")]);

        var result = await FpyTableReport.Instance.RunAsync(
            source,
            new FpyTableFilter(_oneDay, FpyGranularity.Panel, FpyGroupBy.AoiMachine),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Overall.InspectedCount);
        Assert.Equal(1, result.Overall.GoodAoiCount);
        Assert.Equal(1, result.Overall.GoodDiagnosticCount);
        Assert.Equal(2, result.Overall.GoodAfterRepairCount);
    }

    /// <summary>
    /// Pre-reflow lacks <c>ERROR_TABLE_AR</c> — every tested object
    /// row lands with <see cref="TestedObjectRow.ErrorTableAr"/> = 0.
    /// The DPMO Real numerator is therefore always 0 on pre-reflow;
    /// consumers who need review-adjusted counts must target post-
    /// reflow.
    /// </summary>
    [Fact]
    public async Task DpmoTable_Real_OnPreReflow_IsAlwaysZeroWhenErrorTableArIsZero()
    {
        // Simulate the pre-reflow condition: ErrorTable non-zero,
        // ErrorTableAr forced to 0.
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>
        {
            NewObj(machineId: 10, date: start + 60, errorTable: BitObjectMissing, errorTableAr: 0),
            NewObj(machineId: 10, date: start + 61, errorTable: BitPolarityError, errorTableAr: 0),
            NewObj(machineId: 10, date: start + 62, errorTable: 0, errorTableAr: 0),
        };

        // Denominator comes from CARDS: 3 component test opportunities.
        var source = NewSource(
            ParityDescriptors.PreReflow,
            objects: objects,
            machines: SeedMachines(),
            cards: [NewCard(machineId: 10, date: start + 60, nbTestsOnComp: 3)]);
        var result = await DpmoTableReport.Instance.RunAsync(
            source,
            new DpmoTableFilter(_oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Real, DpmoOpportunity.All),
            TestContext.Current.CancellationToken);

        // 3 opportunities, 0 "real" defects because ErrorTableAr is 0.
        Assert.Equal(3L, result.Overall.OpportunityCount);
        Assert.Equal(0L, result.Overall.DefectBitCount);
        Assert.Equal(0d, result.Overall.DpmoPpm);
    }

    /// <summary>
    /// Post-reflow does populate <c>ERROR_TABLE_AR</c>, so the same
    /// three objects with a review-adjusted mask yield a non-zero
    /// Real DPMO. Pairs with
    /// <see cref="DpmoTable_Real_OnPreReflow_IsAlwaysZeroWhenErrorTableArIsZero"/>
    /// to bound the delta.
    /// </summary>
    [Fact]
    public async Task DpmoTable_Real_OnPostReflow_UsesErrorTableAr()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        var objects = new List<TestedObjectRow>
        {
            NewObj(machineId: 10, date: start + 60, errorTable: BitObjectMissing, errorTableAr: BitObjectMissing),
            NewObj(machineId: 10, date: start + 61, errorTable: BitPolarityError, errorTableAr: 0),
            NewObj(machineId: 10, date: start + 62, errorTable: 0, errorTableAr: 0),
        };

        // Denominator comes from CARDS: 3 component test opportunities.
        var source = NewSource(
            ParityDescriptors.PostReflow,
            objects: objects,
            machines: SeedMachines(),
            cards: [NewCard(machineId: 10, date: start + 60, nbTestsOnComp: 3)]);
        var result = await DpmoTableReport.Instance.RunAsync(
            source,
            new DpmoTableFilter(_oneDay, DpmoGroupBy.AoiMachine, DpmoNumerator.Real, DpmoOpportunity.All),
            TestContext.Current.CancellationToken);

        // 3 opportunities, 1 "real" defect bit (object-missing survived review).
        Assert.Equal(3L, result.Overall.OpportunityCount);
        Assert.Equal(1L, result.Overall.DefectBitCount);
        Assert.Equal(1_000_000d / 3, result.Overall.DpmoPpm);
    }

    // -------------------------------------------------------------------------
    // Seed helpers
    // -------------------------------------------------------------------------

    private static FakeAoiSource NewSource(
        SourceDescriptor descriptor,
        IReadOnlyList<PanelRow>? panels = null,
        IReadOnlyList<Machine>? machines = null,
        IReadOnlyList<TestedObjectRow>? objects = null,
        IReadOnlyList<CardRow>? cards = null) =>
        new(descriptor)
        {
            SeededPanels = panels ?? [],
            SeededMachines = machines ?? [],
            SeededTestedObjects = objects ?? [],
            SeededCards = cards ?? [],
        };

    // CARDS row carrying the DPMO/PPM opportunity denominator
    // (Nb_Of_Tests_On_Comp). Opportunities come from cards, never from a
    // (defect-only) tested-object row count.
    private static CardRow NewCard(int machineId, int date, int nbTestsOnComp) =>
        new(
            PanelId: 1,
            CardIdOnPanel: 1,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
            ProductId: 500,
            PanelNumericDate: date,
            NbOfTestsOnComp: nbTestsOnComp);

    private static IReadOnlyList<PanelRow> SeedYieldPanels()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        return
        [
            Panel(1, machineId: 10, date: start + 60, status: 1),
            Panel(2, machineId: 10, date: start + 120, status: 1),
            Panel(3, machineId: 10, date: start + 180, status: 2),
            Panel(4, machineId: 11, date: start + 60, status: -1),
            Panel(5, machineId: 11, date: start + 120, status: 0),
        ];
    }

    private static IReadOnlyList<Machine> SeedMachines() =>
    [
        new Machine(MachineId: 10, MachineType: 2, MachineName: "AOI-10", MachineTypeName: "AOI"),
        new Machine(MachineId: 11, MachineType: 2, MachineName: "AOI-11", MachineTypeName: "AOI"),
    ];

    private static IReadOnlyList<TestedObjectRow> SeedDefectObjects()
    {
        var start = (int)_oneDay.StartEpochSeconds;
        // Two machines, mixed defect bits, ErrorTableAr == ErrorTable so
        // both post- and pre-reflow parity math is unambiguous when
        // callers ask for AOI numerator.
        return
        [
            NewObj(10, start + 60, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
            NewObj(10, start + 61, 0, 0),
            NewObj(10, start + 62, BitSolderJoint, BitSolderJoint),
            NewObj(11, start + 70, BitObjectMissing, BitObjectMissing),
            NewObj(11, start + 71, 0, 0),
        ];
    }

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

    private static TestedObjectRow NewObj(int machineId, int date, long errorTable, long errorTableAr) =>
        new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: date,
            ObjectTypeId: ComponentType,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: 500,
            PanelNumericDate: date,
            Topology: null,
            PartNumberName: null,
            JedecName: null);
}
