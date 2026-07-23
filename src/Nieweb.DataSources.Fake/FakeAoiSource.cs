using System.Runtime.CompilerServices;

namespace Nieweb.DataSources.Fake;

/// <summary>
/// Deterministic in-memory <see cref="IAoiSource"/> used by the
/// Playwright end-to-end harness so a smoke run can exercise the
/// panel-yield pipeline without touching a real Superviseur database.
///
/// Not registered in production hosts - see
/// <see cref="DependencyInjection.FakeAoiSourceServiceCollectionExtensions"/>
/// which only wires the source when
/// <c>Nieweb:Aoi:Fake:Enabled</c> is true.
/// </summary>
public sealed class FakeAoiSource : IAoiSource, IPinLevelSource
{
    /// <summary>Fixed source id, referenced from the E2E spec URLs.</summary>
    public const string SourceId = "fake";

    private readonly IReadOnlyList<PanelRow> _panels;
    private readonly IReadOnlyList<Machine> _machines;
    private readonly IReadOnlyList<Product> _products;
    private readonly IReadOnlyList<Recipe> _recipes;
    private readonly IReadOnlyList<TestedObjectRow> _testedObjects;
    private readonly IReadOnlyList<PinRow> _pins;

    /// <summary>
    /// Builds the singleton with the canonical E2E fixture: one machine,
    /// one product, one recipe, and ten panels on 2026-01-15 UTC (five
    /// clean, five with a defect - FPY = 50%). Each panel carries 20
    /// tested objects (16 components + 4 paste pads) with a
    /// deterministic defect distribution across bits 1 / 2 / 3 / 8 / 9
    /// so component-level reports (DPMO table, Pareto) have a
    /// realistic vital-few shape to render.
    /// </summary>
    public FakeAoiSource()
    {
        Descriptor = new SourceDescriptor(
            Id: SourceId,
            DisplayName: "Fake AOI (E2E fixture)",
            SchemaVersion: "5.0",
            Caps: Capabilities.IsLastInspectionFilter | Capabilities.PinLevel);

        _machines =
        [
            // MachineType 1 = AOI in the Superviseur enum (see
            // IAoiSource.ListMachinesAsync docstring). The fake fixture
            // mirrors that so the seed is schema-accurate.
            new Machine(MachineId: 1, MachineType: 1, MachineName: "AOI-E2E-1", MachineTypeName: "Vision3D CR4"),
        ];
        _products =
        [
            new Product(ProductId: 1, ProductName: "E2E Product", Revision: "A", Description: "Playwright fixture product"),
        ];
        _recipes =
        [
            new Recipe(
                RecipeId: 1,
                FileName: "E2E-Recipe",
                ProductId: 1,
                Author: "e2e",
                InspectedSideNb: 1,
                InspectedSideName: "Top",
                Customer: null,
                ProductionStep: null,
                VariantName: null),
        ];

        // 2026-01-15 08:00:00 UTC = 1768464000 seconds since epoch.
        // Panels are 15 min apart, so the last one lands at 10:15 UTC.
        const int baseEpoch = 1768464000;
        var panels = new List<PanelRow>(capacity: 10);
        for (var i = 0; i < 10; i++)
        {
            var isDefective = i >= 5;
            panels.Add(new PanelRow(
                PanelId: 100 + i,
                MachineId: 1,
                LaneNumber: 1,
                PanelBarCode: $"E2E-{i:D3}",
                PanelNumericDate: baseEpoch + (i * 900), // 15 min apart
                NbOfValidCards: 1,
                TestTime: 6.5,
                PanelStatus: isDefective ? 2 : 0,
                AnomalyBr: isDefective ? 1 : 0,
                AnomalyAr: 0,
                HasBeenReviewed: false,
                NbOfTestedObject: 42,
                NbOfErrorObject: isDefective ? 1 : 0,
                OperatorId: null,
                ProductId: 1,
                RecipeId: 1));
        }
        _panels = panels;
        _testedObjects = BuildTestedObjects(panels);
        _pins = BuildPins(_testedObjects);
    }

    public SourceDescriptor Descriptor { get; }

    public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct)
    {
        var latest = _panels
            .Select(p => (DateTime?)DateTimeOffset.FromUnixTimeSeconds(p.PanelNumericDate).UtcDateTime)
            .DefaultIfEmpty(null)
            .Max();
        return Task.FromResult(latest);
    }

    public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = FilterPanels(query).ToList();
        return Task.FromResult(new Page<PanelRow, PanelCursor>(rows, NextCursor: null, HasMore: false));
    }

    public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct)
        => Task.FromResult(new Page<CardRow, CardCursor>(FilterCards(query).ToList(), NextCursor: null, HasMore: false));

    public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = FilterTestedObjects(query).ToList();
        return Task.FromResult(new Page<TestedObjectRow, TestedObjectCursor>(rows, NextCursor: null, HasMore: false));
    }

    public async IAsyncEnumerable<PanelRow> StreamPanelsAsync(
        PanelQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var panel in FilterPanels(query))
        {
            ct.ThrowIfCancellationRequested();
            yield return panel;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<CardRow> StreamCardsAsync(
        CardQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var card in FilterCards(query))
        {
            ct.ThrowIfCancellationRequested();
            yield return card;
            await Task.Yield();
        }
    }

    // Component-level reports (DPMO, Pareto) stream tested objects
    // through the same window/machine/product/recipe filter as
    // panels and cards; defect distribution is seeded in
    // BuildTestedObjects below.
    public async IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(
        TestedObjectQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var obj in FilterTestedObjects(query))
        {
            ct.ThrowIfCancellationRequested();
            yield return obj;
            await Task.Yield();
        }
    }

    public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
        => Task.FromResult(_machines);

    public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
        => Task.FromResult(_products);

    public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
        => Task.FromResult(_recipes);

    // ---- Traceability drill-down (TC1) ------------------------------------

    public Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct)
        => Task.FromResult<PanelRow?>(FindPanel(panelId));

    public Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        // Return the most recent inspection matching the barcode (see
        // SqlServerAoiSourceBase.GetPanelByBarcodeAsync for the
        // rationale). Ordinal comparison — barcodes are ASCII on the
        // wire.
        PanelRow? best = null;
        foreach (var panel in _panels)
        {
            if (string.Equals(panel.PanelBarCode, barcode, StringComparison.Ordinal)
                && (best is null || panel.PanelNumericDate > best.PanelNumericDate))
            {
                best = panel;
            }
        }
        return Task.FromResult<PanelRow?>(best);
    }

    public Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct)
    {
        // Fixture topology: every panel has exactly one card. Return
        // an empty list when the panel is unknown so the drill-down
        // endpoint layer can distinguish 404-on-panel from
        // empty-subpanels.
        var parent = FindPanel(panelId);
        if (parent is null)
        {
            return Task.FromResult<IReadOnlyList<CardRow>>([]);
        }

        var row = new CardRow(
            PanelId: parent.PanelId,
            CardIdOnPanel: 1,
            CardStatus: parent.PanelStatus,
            AnomalyBr: parent.AnomalyBr,
            AnomalyAr: parent.AnomalyAr,
            NbOfTestedObject: parent.NbOfTestedObject,
            NbOfErrorObject: parent.NbOfErrorObject,
            MachineId: parent.MachineId,
            ProductId: parent.ProductId,
            PanelNumericDate: parent.PanelNumericDate);
        return Task.FromResult<IReadOnlyList<CardRow>>([row]);
    }

    public Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
        long panelId, int cardIdOnPanel, CancellationToken ct)
    {
        var rows = new List<TestedObjectRow>();
        foreach (var obj in _testedObjects)
        {
            if (obj.PanelId == panelId && obj.CardIdOnPanel == cardIdOnPanel)
            {
                rows.Add(obj);
            }
        }
        return Task.FromResult<IReadOnlyList<TestedObjectRow>>(rows);
    }

    // ---- IPinLevelSource (TC1) --------------------------------------------

    public Task<IReadOnlyList<PinRow>> ListPinsForObjectAsync(long testedObjectId, CancellationToken ct)
    {
        var rows = new List<PinRow>();
        foreach (var pin in _pins)
        {
            if (pin.TestedObjectId == testedObjectId)
            {
                rows.Add(pin);
            }
        }
        return Task.FromResult<IReadOnlyList<PinRow>>(rows);
    }

    private IEnumerable<PanelRow> FilterPanels(PanelQuery query)
    {
        foreach (var panel in _panels)
        {
            if (panel.PanelNumericDate < query.Window.StartEpochSeconds)
            {
                continue;
            }
            if (panel.PanelNumericDate >= query.Window.EndEpochSecondsExclusive)
            {
                continue;
            }
            if (query.MachineIds is { Count: > 0 } && !query.MachineIds.Contains(panel.MachineId))
            {
                continue;
            }
            if (query.ProductIds is { Count: > 0 } && !query.ProductIds.Contains(panel.ProductId))
            {
                continue;
            }
            yield return panel;
        }
    }

    // Every fixture panel has exactly one card (matching the fake
    // fixture's simple topology). Card status mirrors panel status so
    // the board-level FPY table returns the same shape as the
    // panel-level table on this source.
    private IEnumerable<CardRow> FilterCards(CardQuery query)
    {
        foreach (var panel in FilterPanelsForCards(query))
        {
            yield return new CardRow(
                PanelId: panel.PanelId,
                CardIdOnPanel: 1,
                CardStatus: panel.PanelStatus,
                AnomalyBr: panel.AnomalyBr,
                AnomalyAr: panel.AnomalyAr,
                NbOfTestedObject: panel.NbOfTestedObject,
                NbOfErrorObject: panel.NbOfErrorObject,
                MachineId: panel.MachineId,
                ProductId: panel.ProductId,
                PanelNumericDate: panel.PanelNumericDate);
        }
    }

    private IEnumerable<PanelRow> FilterPanelsForCards(BaseQuery query)
    {
        foreach (var panel in _panels)
        {
            if (panel.PanelNumericDate < query.Window.StartEpochSeconds)
            {
                continue;
            }
            if (panel.PanelNumericDate >= query.Window.EndEpochSecondsExclusive)
            {
                continue;
            }
            if (query.MachineIds is { Count: > 0 } && !query.MachineIds.Contains(panel.MachineId))
            {
                continue;
            }
            if (query.ProductIds is { Count: > 0 } && !query.ProductIds.Contains(panel.ProductId))
            {
                continue;
            }
            yield return panel;
        }
    }

    private IEnumerable<TestedObjectRow> FilterTestedObjects(TestedObjectQuery query)
    {
        foreach (var obj in _testedObjects)
        {
            if (obj.PanelNumericDate < query.Window.StartEpochSeconds)
            {
                continue;
            }
            if (obj.PanelNumericDate >= query.Window.EndEpochSecondsExclusive)
            {
                continue;
            }
            if (query.MachineIds is { Count: > 0 } && !query.MachineIds.Contains(obj.MachineId))
            {
                continue;
            }
            if (query.ProductIds is { Count: > 0 } && !query.ProductIds.Contains(obj.ProductId))
            {
                continue;
            }
            yield return obj;
        }
    }

    private PanelRow? FindPanel(long panelId)
    {
        foreach (var panel in _panels)
        {
            if (panel.PanelId == panelId)
            {
                return panel;
            }
        }
        return null;
    }

    // ObjectTypeId codes from the vit-aoi-database skill.
    private const int ComponentObjectType = 0x01;
    private const int PastePadObjectType = 0x10;

    // Defect bit masks (see Nieweb.Reports.Common.Defects.DefectBit).
    private const long BitObjectMissing = 1L << 0;   // bit 1
    private const long BitPolarityError = 1L << 1;   // bit 2
    private const long BitSolderJoint = 1L << 2;     // bit 3
    private const long BitTombstone = 1L << 7;       // bit 8
    private const long BitTilt = 1L << 8;            // bit 9

    /// <summary>
    /// Emits 20 tested-object rows per panel (16 components + 4 paste
    /// pads) with a deterministic distribution of reference designators,
    /// part numbers, JEDEC packages, and — on the five defective panels
    /// — five different defect bits. Chosen so the Defect-axis Pareto
    /// has a textbook vital-few / trivial-many shape:
    ///   * bit 1 Object missing  — 5 occurrences
    ///   * bit 3 Solder joint    — 4 occurrences
    ///   * bit 2 Polarity error  — 3 occurrences
    ///   * bit 8 Tombstone       — 2 occurrences
    ///   * bit 9 Tilt            — 1 occurrence
    /// (Panel 8, object R1 carries two bits so total set bits = 15
    /// across 200 opportunities → overall DPMO = 75 000 PPM.)
    /// </summary>
    private static List<TestedObjectRow> BuildTestedObjects(List<PanelRow> panels)
    {
        var rows = new List<TestedObjectRow>(capacity: panels.Count * 20);

        foreach (var panel in panels)
        {
            for (var i = 0; i < 20; i++)
            {
                var (typeId, topology, partNumber, jedec) = LayoutFor(i);
                var (errAoi, errReal) = DefectFor(panel.PanelId, i);
                var status = errAoi == 0L ? 0 : 1;
                var (dx, dy, dth, dz, ds) = DeviationFor(panel.PanelId, i);

                rows.Add(new TestedObjectRow(
                    PanelId: panel.PanelId,
                    CardIdOnPanel: 1,
                    ObjectId: (i + 1) * 10,
                    ObjectTypeId: typeId,
                    ErrorTable: errAoi,
                    ErrorTableAr: errReal,
                    Status: status,
                    MachineId: panel.MachineId,
                    ProductId: panel.ProductId,
                    PanelNumericDate: panel.PanelNumericDate,
                    Topology: topology,
                    PartNumberName: partNumber,
                    JedecName: jedec,
                    DeltaXUm: dx,
                    DeltaYUm: dy,
                    DeltaThetaDeg: dth,
                    DeltaThicknessUm: dz,
                    DeltaSurface: ds));
            }
        }

        return rows;
    }

    private static (int TypeId, string Topology, string? PartNumber, string? Jedec) LayoutFor(int i)
    {
        return i switch
        {
            >= 0 and <= 7 => (ComponentObjectType, $"R{i + 1}", "RES-10K", "0603"),
            >= 8 and <= 11 => (ComponentObjectType, $"C{i - 7}", "CAP-100N", "0603"),
            12 => (ComponentObjectType, "U1", "MCU-STM32", "QFN-48"),
            13 => (ComponentObjectType, "U2", "MCU-STM32", "QFN-48"),
            14 => (ComponentObjectType, "U3", "IC-LDO", "SOT-23"),
            15 => (ComponentObjectType, "U4", "IC-LDO", "SOT-23"),
            _ => (PastePadObjectType, $"PAD{i - 15}", null, null),
        };
    }

    private static (long ErrAoi, long ErrReal) DefectFor(int panelId, int i)
    {
        // Only defective panels (barcode E2E-005..E2E-009 → PanelId
        // 105..109) carry any defects. Both Error_Table and
        // Error_Table_AR are set to the same value so the AOI, Real,
        // and (empty) Dummy numerators all agree in the smoke fixture.
        var mask = (panelId, i) switch
        {
            (105, 0) => BitObjectMissing,
            (105, 5) => BitSolderJoint,
            (105, 12) => BitTombstone,
            (106, 0) => BitObjectMissing,
            (106, 5) => BitSolderJoint,
            (106, 8) => BitPolarityError,
            (107, 1) => BitObjectMissing,
            (107, 9) => BitPolarityError,
            (107, 12) => BitTombstone,
            (108, 0) => BitObjectMissing | BitSolderJoint,
            (108, 13) => BitTilt,
            (109, 2) => BitObjectMissing,
            (109, 5) => BitSolderJoint,
            (109, 8) => BitPolarityError,
            _ => 0L,
        };
        return (mask, mask);
    }

    /// <summary>
    /// Deterministic pseudo-random deviation values for the CR2
    /// Deviation chart. Uses an LCG seeded by
    /// <c>(panelId, objectIndex)</c> so a fixture rebuild reproduces
    /// byte-identical values. Distribution is roughly normal centred on
    /// zero with an occasional out-of-tolerance outlier so bin counts,
    /// mean, and ±3σ overlays all have non-trivial values to assert.
    /// </summary>
    private static (double DxUm, double DyUm, double DthetaDeg, double DzUm, double DsRatio) DeviationFor(
        int panelId, int i)
    {
        var seed = unchecked((uint)((panelId * 397) ^ i));
        // Two independent samples via Box–Muller, then scaled to
        // realistic AOI magnitudes.
        var dx = SampleNormal(ref seed) * 20.0;   // µm  σ≈20
        var dy = SampleNormal(ref seed) * 20.0;   // µm  σ≈20
        var dtheta = SampleNormal(ref seed) * 0.5;// deg σ≈0.5
        var dz = SampleNormal(ref seed) * 15.0;   // µm  σ≈15
        var ds = 1.0 + SampleNormal(ref seed) * 0.05; // ratio, centred at 1.0
        return (Round(dx), Round(dy), Round(dtheta), Round(dz), Round(ds));

        static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
    }

    private static double SampleNormal(ref uint state)
    {
        // Marsaglia polar: pair of independent normals per iteration.
        while (true)
        {
            var u = NextUnitFloat(ref state) * 2.0 - 1.0;
            var v = NextUnitFloat(ref state) * 2.0 - 1.0;
            var s = u * u + v * v;
            if (s > 0 && s < 1)
            {
                return u * Math.Sqrt(-2.0 * Math.Log(s) / s);
            }
        }
    }

    private static double NextUnitFloat(ref uint state)
    {
        // Numerical Recipes minimal LCG (period 2^32) — plenty of
        // determinism for a 5-panel × 20-object fixture.
        state = unchecked(state * 1_664_525u + 1_013_904_223u);
        return (state >> 8) / (double)(1u << 24);
    }

    /// <summary>
    /// Emits four pins per component-typed tested object (one per
    /// side N/E/S/W). Paste-pad objects (<c>ObjectTypeId = 16</c>) get
    /// zero pins — pads have no pin structure in the real
    /// Superviseur schema either. Pin defect bits are inherited from
    /// the parent tested object so drill-down FPY is consistent with
    /// component-level FPY.
    /// </summary>
    private static List<PinRow> BuildPins(IReadOnlyList<TestedObjectRow> objects)
    {
        var pins = new List<PinRow>();
        long nextPinId = 1;
        foreach (var obj in objects)
        {
            // Paste pads carry no pins in the real DB either.
            if (obj.ObjectTypeId != ComponentObjectType)
            {
                continue;
            }
            for (var side = 0; side < 4; side++)
            {
                pins.Add(new PinRow(
                    PinId: nextPinId++,
                    TestedObjectId: obj.ObjectId,
                    ComponentSide: side,
                    PinIndexOnSide: 0,
                    // IPC index is deterministic per pin so drill-down
                    // rendering has stable order.
                    IpcPinNb: side + 1,
                    // Only one pin per object carries the defect —
                    // side 0 (N) — so the pin-level table is faithful
                    // to how a real AOI stores the bit on the
                    // offending joint rather than on every pin.
                    ErrorTable: side == 0 ? obj.ErrorTable : 0L,
                    ErrorTableAr: side == 0 ? obj.ErrorTableAr : 0L,
                    ReviewSanction: 0));
            }
        }
        return pins;
    }
}
