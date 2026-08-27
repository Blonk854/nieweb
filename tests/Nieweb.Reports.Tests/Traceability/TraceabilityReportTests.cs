using Nieweb.DataSources;
using Nieweb.Reports.Tests.Fakes;
using Nieweb.Reports.Traceability;
using Xunit;

namespace Nieweb.Reports.Tests.Traceability;

/// <summary>
/// TC1 — unit tests for the traceability drill-down report.
/// Exercises panel → sub-panel → tested-object → pin lookups against
/// an in-memory <see cref="FakeAoiSource"/>. A separate
/// <see cref="PinAwareFakeSource"/> is used to prove the
/// <see cref="IPinLevelSource"/> branch materialises pins.
/// </summary>
public sealed class TraceabilityReportTests
{
    private const int PanelId = 42;
    // 2026-06-01 12:00:00 UTC in ANSI time_t.
    private const int PanelEpoch = 1_780_660_800;

    private static readonly PanelRow Panel = new(
        PanelId: PanelId,
        MachineId: 1,
        LaneNumber: 1,
        PanelBarCode: "BC-42",
        PanelNumericDate: PanelEpoch,
        NbOfValidCards: 1,
        TestTime: 5.0,
        PanelStatus: 0,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: false,
        NbOfTestedObject: 3,
        NbOfErrorObject: 0,
        OperatorId: null,
        ProductId: 1,
        RecipeId: 1);

    private static readonly CardRow Card = new(
        PanelId: PanelId,
        CardIdOnPanel: 1,
        CardStatus: 0,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: 3,
        NbOfErrorObject: 0,
        MachineId: 1,
        ProductId: 1,
        PanelNumericDate: PanelEpoch);

    private static readonly TestedObjectRow Obj = new(
        PanelId: PanelId,
        CardIdOnPanel: 1,
        ObjectId: 10,
        ObjectTypeId: 1,
        ErrorTable: 0,
        ErrorTableAr: 0,
        Status: 0,
        MachineId: 1,
        ProductId: 1,
        PanelNumericDate: PanelEpoch,
        Topology: "R1",
        PartNumberName: "RES-10K",
        JedecName: "0603");

    private static readonly PinRow Pin = new(
        PinId: 1000,
        TestedObjectId: 10,
        ComponentSide: 0,
        PinIndexOnSide: 0,
        IpcPinNb: 1,
        ErrorTable: 0,
        ErrorTableAr: 0,
        ReviewSanction: 0);

    private static FakeAoiSource NewSource(bool withPins = false)
    {
        var descriptor = new SourceDescriptor(
            Id: "fake",
            DisplayName: "Fake",
            SchemaVersion: "5.0",
            Caps: withPins ? Capabilities.PinLevel : Capabilities.None);
        return new FakeAoiSource(descriptor)
        {
            SeededPanels = [Panel],
            SeededCards = [Card],
            SeededTestedObjects = [Obj],
        };
    }

    [Fact]
    public async Task GetPanelDetailAsync_returns_panel_and_utc_when_found()
    {
        var source = NewSource();
        var result = await TraceabilityReport.GetPanelDetailAsync(source, PanelId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(PanelId, result!.Panel.PanelId);
        Assert.Equal(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc), result.PanelUtc);
    }

    [Fact]
    public async Task GetPanelDetailAsync_returns_null_for_unknown_panel()
    {
        var source = NewSource();
        var result = await TraceabilityReport.GetPanelDetailAsync(source, 999, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPanelDetailByBarcodeAsync_returns_latest_matching_panel()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .GetPanelDetailByBarcodeAsync(source, "BC-42", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(PanelId, result!.Panel.PanelId);
    }

    [Fact]
    public async Task GetPanelDetailByBarcodeAsync_returns_null_for_unknown_barcode()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .GetPanelDetailByBarcodeAsync(source, "NOPE", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListSubpanelsForPanelAsync_returns_cards_for_known_panel()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .ListSubpanelsForPanelAsync(source, PanelId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result!.Value.Cards);
        Assert.Equal(1, result.Value.Cards[0].CardIdOnPanel);
    }

    [Fact]
    public async Task ListSubpanelsForPanelAsync_returns_null_for_unknown_panel()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .ListSubpanelsForPanelAsync(source, 999, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSubpanelDetailAsync_returns_breadcrumb_and_card()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .GetSubpanelDetailAsync(source, PanelId, 1, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(PanelId, result!.Panel.PanelId);
        Assert.Equal(1, result.Card.CardIdOnPanel);
    }

    [Fact]
    public async Task GetSubpanelDetailAsync_returns_null_when_card_absent()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .GetSubpanelDetailAsync(source, PanelId, 99, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListTestedObjectsForSubpanelAsync_returns_objects_for_known_subpanel()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .ListTestedObjectsForSubpanelAsync(source, PanelId, 1, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result!.Value.Objects);
        Assert.Equal(10, result.Value.Objects[0].ObjectId);
    }

    [Fact]
    public async Task GetTestedObjectDetailAsync_returns_object_without_pins_when_source_lacks_pin_level()
    {
        var source = NewSource(withPins: false);
        var result = await TraceabilityReport
            .GetTestedObjectDetailAsync(source, PanelId, 1, 10, CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(result!.PinsAvailable);
        Assert.Empty(result.Pins);
    }

    [Fact]
    public async Task GetTestedObjectDetailAsync_returns_pins_when_source_implements_pin_level()
    {
        var source = new PinAwareFakeSource(NewSource(withPins: true), [Pin]);
        var result = await TraceabilityReport
            .GetTestedObjectDetailAsync(source, PanelId, 1, 10, CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result!.PinsAvailable);
        Assert.Single(result.Pins);
        Assert.Equal(1000L, result.Pins[0].PinId);
    }

    [Fact]
    public async Task GetTestedObjectDetailAsync_returns_null_when_object_absent()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .GetTestedObjectDetailAsync(source, PanelId, 1, 999, CancellationToken.None);
        Assert.Null(result);
    }

    // ------------------------------------------------------------------
    // TC5 Phase C — failed-objects-for-panel drill-down.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListFailedObjectsForPanelAsync_returns_null_for_unknown_panel()
    {
        var source = NewSource();
        var result = await TraceabilityReport
            .ListFailedObjectsForPanelAsync(source, 999, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListFailedObjectsForPanelAsync_returns_empty_when_panel_has_no_failures()
    {
        // NewSource() seeds one CardRow with NbOfErrorObject = 0 and
        // one TestedObjectRow with ErrorTableAr = 0 — the default
        // DIM fallback should skip the card entirely (perf branch)
        // and return an empty list wrapped in a valid tuple.
        var source = NewSource();
        var result = await TraceabilityReport
            .ListFailedObjectsForPanelAsync(source, PanelId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(PanelId, result!.Value.Panel.Panel.PanelId);
        Assert.Empty(result.Value.Objects);
    }

    [Fact]
    public async Task ListFailedObjectsForPanelAsync_aggregates_across_subpanels_and_filters_on_error_table_ar()
    {
        // Two subpanels, both flagged NbOfErrorObject > 0 so the DIM
        // fallback visits them. Four tested objects total; only two
        // have ErrorTableAr != 0 and should be returned. The
        // ErrorTableAr == 0 rows include one that has raw
        // ErrorTable != 0 (false call cleared during review) — it
        // must be excluded.
        var descriptor = new SourceDescriptor(
            Id: "fake",
            DisplayName: "Fake",
            SchemaVersion: "5.0",
            Caps: Capabilities.None);
        var card1 = Card with { CardIdOnPanel = 1, NbOfErrorObject = 1 };
        var card2 = Card with { CardIdOnPanel = 2, NbOfErrorObject = 1 };
        var passing = Obj with { CardIdOnPanel = 1, ObjectId = 10, ErrorTable = 0, ErrorTableAr = 0 };
        var falseCall = Obj with { CardIdOnPanel = 1, ObjectId = 11, ErrorTable = 4, ErrorTableAr = 0 };
        var failed1 = Obj with { CardIdOnPanel = 1, ObjectId = 12, ErrorTable = 8, ErrorTableAr = 8 };
        var failed2 = Obj with { CardIdOnPanel = 2, ObjectId = 20, ErrorTable = 16, ErrorTableAr = 16 };
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels = [Panel],
            SeededCards = [card1, card2],
            SeededTestedObjects = [passing, falseCall, failed1, failed2],
        };

        var result = await TraceabilityReport
            .ListFailedObjectsForPanelAsync(source, PanelId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(PanelId, result!.Value.Panel.Panel.PanelId);
        var objects = result.Value.Objects;
        Assert.Equal(2, objects.Count);
        // DIM fallback iterates cards in seed order, then per-card
        // tested-object order — so failed1 (card 1) precedes
        // failed2 (card 2).
        Assert.Equal(12, objects[0].ObjectId);
        Assert.Equal(20, objects[1].ObjectId);
    }

    [Fact]
    public async Task ListFailedObjectsForPanelAsync_skips_subpanels_with_zero_error_count()
    {
        // A card with NbOfErrorObject = 0 must NOT trigger the
        // per-card ListTestedObjectsForSubpanelAsync round-trip,
        // regardless of what tested-object rows might be seeded
        // for it. We prove the skip-branch by attaching a failing
        // object to a clean card (nonsensical in production but a
        // watertight assertion here) — it should NOT appear.
        var descriptor = new SourceDescriptor(
            Id: "fake",
            DisplayName: "Fake",
            SchemaVersion: "5.0",
            Caps: Capabilities.None);
        var cleanCard = Card with { CardIdOnPanel = 1, NbOfErrorObject = 0 };
        var stragglerOnCleanCard = Obj with { CardIdOnPanel = 1, ObjectId = 99, ErrorTable = 1, ErrorTableAr = 1 };
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels = [Panel],
            SeededCards = [cleanCard],
            SeededTestedObjects = [stragglerOnCleanCard],
        };

        var result = await TraceabilityReport
            .ListFailedObjectsForPanelAsync(source, PanelId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!.Value.Objects);
    }

    // ------------------------------------------------------------------
    // TC2 — cross-DB board trace by barcode.
    // ------------------------------------------------------------------

    private static FakeAoiSource NewNamedSource(string id, string barcode, int panelId, bool withPins = false)
    {
        var descriptor = new SourceDescriptor(
            Id: id,
            DisplayName: id + " AOI",
            SchemaVersion: withPins ? "5.0" : "4.3.1",
            Caps: withPins ? Capabilities.PinLevel : Capabilities.None);
        var panel = Panel with { PanelId = panelId, PanelBarCode = barcode };
        var card = Card with { PanelId = panelId };
        return new FakeAoiSource(descriptor)
        {
            SeededPanels = [panel],
            SeededCards = [card],
            SeededTestedObjects = [Obj with { PanelId = panelId }],
        };
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_returns_one_stage_per_source_with_matches()
    {
        var post = new PinAwareFakeSource(NewNamedSource("postreflow", "SN-777", 100, withPins: true), [Pin]);
        var pre = NewNamedSource("prereflow", "SN-777", 200);

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [post, pre], "SN-777", CancellationToken.None);

        Assert.NotNull(trace);
        Assert.Equal("SN-777", trace!.Barcode);
        Assert.Equal(2, trace.Stages.Count);

        var postStage = trace.Stages.Single(s => s.SourceId == "postreflow");
        Assert.NotEmpty(postStage.Sides);
        Assert.Equal(100, postStage.Sides[0].Panel.Panel.PanelId);
        Assert.True(postStage.PinsAvailable);
        Assert.Single(postStage.Sides[0].Cards);
        Assert.Null(postStage.Error);

        var preStage = trace.Stages.Single(s => s.SourceId == "prereflow");
        Assert.NotEmpty(preStage.Sides);
        Assert.Equal(200, preStage.Sides[0].Panel.Panel.PanelId);
        Assert.False(preStage.PinsAvailable);
        Assert.Null(preStage.Error);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_returns_null_panel_when_barcode_missing_from_one_source()
    {
        var post = NewNamedSource("postreflow", "SN-999", 100);
        var pre = NewNamedSource("prereflow", "SN-OTHER", 200);

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [post, pre], "SN-999", CancellationToken.None);

        Assert.NotNull(trace);
        Assert.Equal(2, trace!.Stages.Count);

        var postStage = trace.Stages.Single(s => s.SourceId == "postreflow");
        Assert.NotEmpty(postStage.Sides);
        Assert.Null(postStage.Error);

        var preStage = trace.Stages.Single(s => s.SourceId == "prereflow");
        Assert.Empty(preStage.Sides);
        Assert.Null(preStage.Error);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_returns_all_null_panels_when_barcode_unknown()
    {
        var post = NewNamedSource("postreflow", "SN-A", 100);
        var pre = NewNamedSource("prereflow", "SN-B", 200);

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [post, pre], "SN-MISSING", CancellationToken.None);

        Assert.NotNull(trace);
        Assert.All(trace!.Stages, s =>
        {
            Assert.Empty(s.Sides);
            Assert.Null(s.Error);
        });
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_isolates_source_errors_and_returns_other_stages()
    {
        var post = NewNamedSource("postreflow", "SN-42", 100);
        var pre = new ThrowingAoiSource(
            new SourceDescriptor("prereflow", "Pre-reflow", "4.3.1", Capabilities.None),
            new InvalidOperationException("simulated outage"));

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [post, pre], "SN-42", CancellationToken.None);

        Assert.NotNull(trace);
        var postStage = trace!.Stages.Single(s => s.SourceId == "postreflow");
        Assert.NotEmpty(postStage.Sides);
        Assert.Null(postStage.Error);

        var preStage = trace.Stages.Single(s => s.SourceId == "prereflow");
        Assert.Empty(preStage.Sides);
        Assert.Equal("simulated outage", preStage.Error);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_returns_null_when_no_sources_configured()
    {
        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            Array.Empty<IAoiSource>(), "SN-1", CancellationToken.None);
        Assert.Null(trace);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_throws_on_empty_barcode()
    {
        var source = NewSource();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            TraceabilityReport.GetBoardByBarcodeAsync([source], "   ", CancellationToken.None));
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_enriches_panel_with_machine_and_operator_names()
    {
        // Build a source whose panel references machine 7 and
        // operator 13, then seed the corresponding roster rows so
        // TraceabilityReport.ProbeStageAsync can resolve both names
        // in one Task.WhenAll fan-out.
        const int panelId = 500;
        var descriptor = new SourceDescriptor(
            Id: "postreflow",
            DisplayName: "Post-reflow AOI",
            SchemaVersion: "5.0",
            Caps: Capabilities.None);
        var panel = Panel with
        {
            PanelId = panelId,
            PanelBarCode = "SN-ENRICH",
            MachineId = 7,
            OperatorId = 13,
        };
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels = [panel],
            SeededCards = [Card with { PanelId = panelId }],
            SeededMachines =
            [
                new Machine(7, 0, "AOI-Line-A", null),
                new Machine(8, 0, "AOI-Line-B", null),
            ],
            SeededOperators =
            [
                new ReviewOperator(13, "Alice Anderson"),
                new ReviewOperator(14, "Bob Baker"),
            ],
        };

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [source], "SN-ENRICH", CancellationToken.None);

        Assert.NotNull(trace);
        var stage = Assert.Single(trace!.Stages);
        var side = Assert.Single(stage.Sides);
        Assert.Equal("AOI-Line-A", side.Panel.MachineName);
        Assert.Equal("Alice Anderson", side.Panel.OperatorName);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_leaves_names_null_when_lookup_yields_no_match()
    {
        // Panel references machine 99 and operator 99 — neither is
        // present in the seeded rosters. The enrichment step must
        // treat the miss as null (not throw, not fabricate a name).
        const int panelId = 501;
        var descriptor = new SourceDescriptor(
            Id: "postreflow",
            DisplayName: "Post-reflow AOI",
            SchemaVersion: "5.0",
            Caps: Capabilities.None);
        var panel = Panel with
        {
            PanelId = panelId,
            PanelBarCode = "SN-NOMATCH",
            MachineId = 99,
            OperatorId = 99,
        };
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels = [panel],
            SeededCards = [Card with { PanelId = panelId }],
            SeededMachines = [new Machine(1, 0, "AOI-Line-A", null)],
            SeededOperators = [new ReviewOperator(1, "Alice Anderson")],
        };

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [source], "SN-NOMATCH", CancellationToken.None);

        var stage = Assert.Single(trace!.Stages);
        var side = Assert.Single(stage.Sides);
        Assert.Null(side.Panel.MachineName);
        Assert.Null(side.Panel.OperatorName);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_leaves_operator_name_null_when_operator_id_is_null()
    {
        // A panel with OperatorId=null (never reviewed) must not
        // trigger the operator roster lookup and must return
        // OperatorName=null so the UI can render its "Not reviewed"
        // placeholder without a spurious id lookup.
        const int panelId = 502;
        var descriptor = new SourceDescriptor(
            Id: "postreflow",
            DisplayName: "Post-reflow AOI",
            SchemaVersion: "5.0",
            Caps: Capabilities.None);
        var panel = Panel with
        {
            PanelId = panelId,
            PanelBarCode = "SN-NULLOP",
            MachineId = 1,
            OperatorId = null,
        };
        var source = new FakeAoiSource(descriptor)
        {
            SeededPanels = [panel],
            SeededCards = [Card with { PanelId = panelId }],
            SeededMachines = [new Machine(1, 0, "AOI-Line-A", null)],
            SeededOperators = [new ReviewOperator(1, "Alice Anderson")],
        };

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [source], "SN-NULLOP", CancellationToken.None);

        var stage = Assert.Single(trace!.Stages);
        var side = Assert.Single(stage.Sides);
        Assert.Equal("AOI-Line-A", side.Panel.MachineName);
        Assert.Null(side.Panel.OperatorName);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_multi_pass_selects_latest_and_lists_priors()
    {
        var panels = new List<PanelRow>();
        var cards = new List<CardRow>();
        for (var i = 0; i < 3; i++)
        {
            var id = 700 + i;
            panels.Add(Panel with
            {
                PanelId = id,
                PanelBarCode = "SN-REPEAT",
                PanelNumericDate = PanelEpoch + (i * 100),
                FaceNumber = 1,
            });
            cards.Add(Card with { PanelId = id });
        }

        var source = new FakeAoiSource(new SourceDescriptor("postreflow", "Post", "5.0", Capabilities.None))
        {
            SeededPanels = panels,
            SeededCards = cards,
        };

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [source], "SN-REPEAT", CancellationToken.None);

        var stage = Assert.Single(trace!.Stages);
        var side = Assert.Single(stage.Sides);
        Assert.Equal(702, side.Panel.Panel.PanelId);
        Assert.Null(side.PinnedPanelId);
        Assert.Equal(2, side.PriorPasses.Count);
        Assert.Equal(701, side.PriorPasses[0].PanelId);
        Assert.Equal(700, side.PriorPasses[1].PanelId);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_two_faces_partition_priors_independently()
    {
        var panels = new List<PanelRow>
        {
            Panel with { PanelId = 1, PanelBarCode = "SN-2SIDE", PanelNumericDate = PanelEpoch, FaceNumber = 1 },
            Panel with { PanelId = 2, PanelBarCode = "SN-2SIDE", PanelNumericDate = PanelEpoch + 10, FaceNumber = 1 },
            Panel with { PanelId = 3, PanelBarCode = "SN-2SIDE", PanelNumericDate = PanelEpoch, FaceNumber = 2 },
            Panel with { PanelId = 4, PanelBarCode = "SN-2SIDE", PanelNumericDate = PanelEpoch + 10, FaceNumber = 2 },
        };
        var cards = panels.Select(p => Card with { PanelId = p.PanelId }).ToList();
        var source = new FakeAoiSource(new SourceDescriptor("postreflow", "Post", "5.0", Capabilities.None))
        {
            SeededPanels = panels,
            SeededCards = cards,
        };

        var pins = new Dictionary<string, int> { ["postreflow"] = 1 };
        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [source], "SN-2SIDE", pins, CancellationToken.None);

        var stage = Assert.Single(trace!.Stages);
        Assert.Equal(2, stage.Sides.Count);

        var face1 = stage.Sides.Single(s => s.FaceNumber == 1);
        Assert.Equal(1, face1.Panel.Panel.PanelId);
        Assert.Equal(1, face1.PinnedPanelId);
        Assert.Single(face1.PriorPasses);
        Assert.Equal(2, face1.PriorPasses[0].PanelId);

        var face2 = stage.Sides.Single(s => s.FaceNumber == 2);
        Assert.Equal(4, face2.Panel.Panel.PanelId);
        Assert.Null(face2.PinnedPanelId);
        Assert.Single(face2.PriorPasses);
        Assert.Equal(3, face2.PriorPasses[0].PanelId);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_caps_at_ten_passes_and_loads_cards_once_per_face()
    {
        var panels = new List<PanelRow>();
        var cards = new List<CardRow>();
        for (var i = 0; i < 12; i++)
        {
            var id = 800 + i;
            panels.Add(Panel with
            {
                PanelId = id,
                PanelBarCode = "SN-MANY",
                PanelNumericDate = PanelEpoch + i,
                FaceNumber = 1,
            });
            cards.Add(Card with { PanelId = id });
        }

        var inner = new FakeAoiSource(new SourceDescriptor("postreflow", "Post", "5.0", Capabilities.None))
        {
            SeededPanels = panels,
            SeededCards = cards,
        };
        var counting = new CardCountingSource(inner);

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [counting], "SN-MANY", CancellationToken.None);

        var stage = Assert.Single(trace!.Stages);
        var side = Assert.Single(stage.Sides);
        Assert.Equal(811, side.Panel.Panel.PanelId); // latest of 12
        Assert.Equal(9, side.PriorPasses.Count); // 10 total − selected
        Assert.Equal(1, counting.CardLoadCount);
    }

    [Fact]
    public async Task GetBoardByBarcodeAsync_stale_pin_falls_back_with_selection_warning()
    {
        var post = NewNamedSource("postreflow", "SN-STALE", 100);
        var pre = NewNamedSource("prereflow", "SN-STALE", 200);
        var pins = new Dictionary<string, int> { ["postreflow"] = 99999 };

        var trace = await TraceabilityReport.GetBoardByBarcodeAsync(
            [post, pre], "SN-STALE", pins, CancellationToken.None);

        Assert.NotNull(trace);
        var postStage = trace!.Stages.Single(s => s.SourceId == "postreflow");
        Assert.Equal(100, postStage.Sides[0].Panel.Panel.PanelId);
        Assert.Null(postStage.Error);
        Assert.NotNull(postStage.SelectionWarning);

        var preStage = trace.Stages.Single(s => s.SourceId == "prereflow");
        Assert.Equal(200, preStage.Sides[0].Panel.Panel.PanelId);
        Assert.Null(preStage.Error);
        Assert.Null(preStage.SelectionWarning);
    }

    private sealed class CardCountingSource : IAoiSource
    {
        private readonly IAoiSource _inner;
        public int CardLoadCount { get; private set; }

        public CardCountingSource(IAoiSource inner) => _inner = inner;

        public SourceDescriptor Descriptor => _inner.Descriptor;
        public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct) => _inner.GetLatestPanelUtcAsync(ct);
        public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct) => _inner.QueryPanelsAsync(query, ct);
        public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct) => _inner.QueryCardsAsync(query, ct);
        public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct) => _inner.QueryTestedObjectsAsync(query, ct);
        public IAsyncEnumerable<PanelRow> StreamPanelsAsync(PanelQuery query, CancellationToken ct) => _inner.StreamPanelsAsync(query, ct);
        public IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery query, CancellationToken ct) => _inner.StreamCardsAsync(query, ct);
        public IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct) => _inner.StreamTestedObjectsAsync(query, ct);
        public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct) => _inner.ListMachinesAsync(ct);
        public Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct) => _inner.ListOperatorsAsync(ct);
        public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct) => _inner.ListProductsAsync(ct);
        public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct) => _inner.ListRecipesAsync(ct);
        public Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct) => _inner.GetPanelByIdAsync(panelId, ct);
        public Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct) => _inner.GetPanelByBarcodeAsync(barcode, ct);
        public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(string barcode, CancellationToken ct) => _inner.ListPanelsByBarcodeAsync(barcode, ct);
        public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(string barcode, int limit, CancellationToken ct) => _inner.ListPanelsByBarcodeAsync(barcode, limit, ct);
        public Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct)
        {
            CardLoadCount++;
            return _inner.ListCardsForPanelAsync(panelId, ct);
        }
        public Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(long panelId, int cardIdOnPanel, CancellationToken ct)
            => _inner.ListTestedObjectsForSubpanelAsync(panelId, cardIdOnPanel, ct);
    }

    /// <summary>
    /// Stub that throws from every lookup — used to prove
    /// per-stage error isolation in TC2.
    /// </summary>
    private sealed class ThrowingAoiSource : IAoiSource
    {
        private readonly Exception _boom;

        public ThrowingAoiSource(SourceDescriptor descriptor, Exception boom)
        {
            Descriptor = descriptor;
            _boom = boom;
        }

        public SourceDescriptor Descriptor { get; }

        public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct) => throw _boom;
        public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery q, CancellationToken ct) => throw _boom;
        public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery q, CancellationToken ct) => throw _boom;
        public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery q, CancellationToken ct) => throw _boom;
        public IAsyncEnumerable<PanelRow> StreamPanelsAsync(PanelQuery q, CancellationToken ct) => throw _boom;
        public IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery q, CancellationToken ct) => throw _boom;
        public IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery q, CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct) => throw _boom;
        public Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct) => throw _boom;
        public Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(string barcode, CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(string barcode, int limit, CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct) => throw _boom;
        public Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
            long panelId, int cardIdOnPanel, CancellationToken ct) => throw _boom;
    }

    /// <summary>
    /// Composes over an inner <see cref="IAoiSource"/> and adds
    /// <see cref="IPinLevelSource"/> support with a fixed pin list so
    /// the traceability report's pin-materialising branch is
    /// exercised without duplicating the whole fake.
    /// </summary>
    private sealed class PinAwareFakeSource : IAoiSource, IPinLevelSource
    {
        private readonly IAoiSource _inner;
        private readonly IReadOnlyList<PinRow> _pins;

        public PinAwareFakeSource(IAoiSource inner, IReadOnlyList<PinRow> pins)
        {
            _inner = inner;
            _pins = pins;
        }

        public SourceDescriptor Descriptor => _inner.Descriptor;

        public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct)
            => _inner.GetLatestPanelUtcAsync(ct);

        public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct)
            => _inner.QueryPanelsAsync(query, ct);

        public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct)
            => _inner.QueryCardsAsync(query, ct);

        public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
            => _inner.QueryTestedObjectsAsync(query, ct);

        public IAsyncEnumerable<PanelRow> StreamPanelsAsync(PanelQuery query, CancellationToken ct)
            => _inner.StreamPanelsAsync(query, ct);

        public IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery query, CancellationToken ct)
            => _inner.StreamCardsAsync(query, ct);

        public IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
            => _inner.StreamTestedObjectsAsync(query, ct);

        public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
            => _inner.ListMachinesAsync(ct);

        public Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct)
            => _inner.ListOperatorsAsync(ct);

        public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
            => _inner.ListProductsAsync(ct);

        public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
            => _inner.ListRecipesAsync(ct);

        public Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct)
            => _inner.GetPanelByIdAsync(panelId, ct);

        public Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct)
            => _inner.GetPanelByBarcodeAsync(barcode, ct);

        public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(string barcode, CancellationToken ct)
            => _inner.ListPanelsByBarcodeAsync(barcode, ct);

        public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(string barcode, int limit, CancellationToken ct)
            => _inner.ListPanelsByBarcodeAsync(barcode, limit, ct);

        public Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct)
            => _inner.ListCardsForPanelAsync(panelId, ct);

        public Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
            long panelId, int cardIdOnPanel, CancellationToken ct)
            => _inner.ListTestedObjectsForSubpanelAsync(panelId, cardIdOnPanel, ct);

        public Task<IReadOnlyList<PinRow>> ListPinsForObjectAsync(long testedObjectId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PinRow>>(
                _pins.Where(p => p.TestedObjectId == testedObjectId).ToList());
    }
}
