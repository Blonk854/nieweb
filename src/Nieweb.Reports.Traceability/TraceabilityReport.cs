using Nieweb.DataSources;

namespace Nieweb.Reports.Traceability;

/// <summary>
/// Panel → sub-panel → tested-object → pin drill-down (TC1).
/// Wraps the traceability helpers on <see cref="IAoiSource"/> so the
/// API layer stays a thin HTTP shell, and so batch/scheduled callers
/// can reuse the exact same materialisation.
/// </summary>
/// <remarks>
/// <para>
/// Every method is a pure function of its inputs — no shared mutable
/// state, no additional I/O beyond the injected
/// <see cref="IAoiSource"/>. This mirrors the pattern used by the
/// other reports in <c>Nieweb.Reports</c>
/// (<c>PanelYieldByLineReport</c>, <c>DpmoTableReport</c>, …) so tests
/// can supply an in-memory source.
/// </para>
/// <para>
/// The methods explicitly do not accept a
/// <c>Nieweb.Reports.Common.DateRange</c>. Traceability is
/// point-lookup by identity — <c>Panel_Id</c>, <c>Card_Number</c>,
/// <c>Tested_Object_Id</c> — not a windowed aggregation, so imposing
/// a window would just push extra work onto the DB engine.
/// </para>
/// </remarks>
public static class TraceabilityReport
{
    /// <summary>
    /// Loads the detail view for a single <c>PANELS</c> row. Returns
    /// <c>null</c> when the panel is unknown to <paramref name="source"/>.
    /// </summary>
    public static async Task<TraceabilityPanel?> GetPanelDetailAsync(
        IAoiSource source,
        int panelId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var panel = await source.GetPanelByIdAsync(panelId, ct).ConfigureAwait(false);
        if (panel is null)
        {
            return null;
        }
        var name = await ResolveProductNameAsync(source, panel.ProductId, ct).ConfigureAwait(false);
        return Materialise(panel, name);
    }

    /// <summary>
    /// Looks up a panel by <c>Panel_Bar_Code</c> and returns the
    /// detail view for the most recent inspection. Returns
    /// <c>null</c> when no panel matches. Barcode is case-sensitive
    /// (matches how AOI Superviseur stores it on the wire).
    /// </summary>
    public static async Task<TraceabilityPanel?> GetPanelDetailByBarcodeAsync(
        IAoiSource source,
        string barcode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        var panel = await source.GetPanelByBarcodeAsync(barcode, ct).ConfigureAwait(false);
        if (panel is null)
        {
            return null;
        }
        var name = await ResolveProductNameAsync(source, panel.ProductId, ct).ConfigureAwait(false);
        return Materialise(panel, name);
    }

    /// <summary>
    /// Returns every sub-panel (<c>CARDS</c>) attached to
    /// <paramref name="panelId"/>. Returns <c>null</c> when the
    /// parent panel does not exist (so the endpoint layer can return
    /// 404). Returns an empty list wrapped in a valid tuple when the
    /// panel exists but has no cards — that is a legitimate state on
    /// a not-yet-fully-processed inspection.
    /// </summary>
    public static async Task<(TraceabilityPanel Panel, IReadOnlyList<CardRow> Cards)?> ListSubpanelsForPanelAsync(
        IAoiSource source,
        int panelId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var panel = await source.GetPanelByIdAsync(panelId, ct).ConfigureAwait(false);
        if (panel is null)
        {
            return null;
        }
        var cards = await source.ListCardsForPanelAsync(panelId, ct).ConfigureAwait(false);
        var name = await ResolveProductNameAsync(source, panel.ProductId, ct).ConfigureAwait(false);
        return (Materialise(panel, name), cards);
    }

    /// <summary>
    /// Loads the sub-panel detail (parent panel breadcrumb + card
    /// row). Returns <c>null</c> when either the panel or the card
    /// does not exist.
    /// </summary>
    public static async Task<TraceabilitySubpanel?> GetSubpanelDetailAsync(
        IAoiSource source,
        int panelId,
        int cardIdOnPanel,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var panel = await source.GetPanelByIdAsync(panelId, ct).ConfigureAwait(false);
        if (panel is null)
        {
            return null;
        }
        var cards = await source.ListCardsForPanelAsync(panelId, ct).ConfigureAwait(false);
        CardRow? card = null;
        foreach (var c in cards)
        {
            if (c.CardIdOnPanel == cardIdOnPanel)
            {
                card = c;
                break;
            }
        }
        if (card is null)
        {
            return null;
        }
        var (_, panelUtc) = Split(panel);
        return new TraceabilitySubpanel(panel, panelUtc, card);
    }

    /// <summary>
    /// Returns every tested object on a given sub-panel. Returns
    /// <c>null</c> when the parent panel or sub-panel does not
    /// exist (so the endpoint layer can return 404).
    /// </summary>
    public static async Task<(TraceabilitySubpanel Subpanel, IReadOnlyList<TestedObjectRow> Objects)?> ListTestedObjectsForSubpanelAsync(
        IAoiSource source,
        int panelId,
        int cardIdOnPanel,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var subpanel = await GetSubpanelDetailAsync(source, panelId, cardIdOnPanel, ct).ConfigureAwait(false);
        if (subpanel is null)
        {
            return null;
        }
        var objects = await source.ListTestedObjectsForSubpanelAsync(panelId, cardIdOnPanel, ct).ConfigureAwait(false);
        return (subpanel, objects);
    }

    /// <summary>
    /// TC5 Phase C — returns every <em>failed</em> tested object on
    /// the given panel, aggregated across every sub-panel. A row is
    /// considered failed when its <c>Error_Table_AR</c> (post-review
    /// defect bitfield) is non-zero, matching the semantics of the
    /// TC5 failed-objects table (raw pre-review AOI opinions that
    /// were subsequently cleared as false calls do not appear).
    /// Returns <c>null</c> when the panel does not exist so the
    /// endpoint layer can return 404; returns an empty
    /// <see cref="IReadOnlyList{T}"/> when the panel exists but has
    /// no failures.
    /// </summary>
    public static async Task<(TraceabilityPanel Panel, IReadOnlyList<TestedObjectRow> Objects)?> ListFailedObjectsForPanelAsync(
        IAoiSource source,
        int panelId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var panel = await source.GetPanelByIdAsync(panelId, ct).ConfigureAwait(false);
        if (panel is null)
        {
            return null;
        }
        var objects = await source.ListFailedTestedObjectsForPanelAsync(panelId, ct).ConfigureAwait(false);
        var name = await ResolveProductNameAsync(source, panel.ProductId, ct).ConfigureAwait(false);
        return (Materialise(panel, name), objects);
    }

    /// <summary>
    /// Loads the tested-object detail. Pin data is populated only
    /// when the source implements <see cref="IPinLevelSource"/>; on
    /// pre-reflow sources <c>Pins</c> is empty and
    /// <c>PinsAvailable</c> is <c>false</c>. Returns <c>null</c>
    /// when the panel, sub-panel, or tested object is unknown.
    /// </summary>
    public static async Task<TraceabilityTestedObject?> GetTestedObjectDetailAsync(
        IAoiSource source,
        int panelId,
        int cardIdOnPanel,
        int testedObjectId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var listing = await ListTestedObjectsForSubpanelAsync(source, panelId, cardIdOnPanel, ct).ConfigureAwait(false);
        if (listing is null)
        {
            return null;
        }
        var (subpanel, objects) = listing.Value;
        TestedObjectRow? match = null;
        foreach (var o in objects)
        {
            if (o.ObjectId == testedObjectId)
            {
                match = o;
                break;
            }
        }
        if (match is null)
        {
            return null;
        }

        IReadOnlyList<PinRow> pins = [];
        var pinsAvailable = false;
        if (source is IPinLevelSource pinSource)
        {
            pins = await pinSource.ListPinsForObjectAsync(match.ObjectId, ct).ConfigureAwait(false);
            pinsAvailable = true;
        }

        return new TraceabilityTestedObject(
            Panel: subpanel.Panel,
            PanelUtc: subpanel.PanelUtc,
            Card: subpanel.Card,
            TestedObject: match,
            Pins: pins,
            PinsAvailable: pinsAvailable);
    }

    /// <summary>
    /// Product cap on prior AOI passes returned per face. Matches the
    /// "operators shouldn't re-run a panel more than 10 times" rule and
    /// bounds the board-trace payload.
    /// </summary>
    public const int PriorPassLimit = 10;

    /// <summary>
    /// TC2 — cross-DB board trace by barcode. Fans the lookup out
    /// across every configured <see cref="IAoiSource"/> and returns
    /// one <see cref="BoardStageTrace"/> per source so the SPA can
    /// render one side-by-side table per stage
    /// (pre-reflow paste / post-reflow AOI / ...).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each source is queried independently and its exception (if
    /// any) is captured on the corresponding stage's
    /// <see cref="BoardStageTrace.Error"/> field. A single-DB outage
    /// therefore never crashes the whole payload — the healthy
    /// stages still return their data. This is deliberate: the SPA
    /// contract for TC2 is "always render every configured stage,
    /// even if only one has data or if the operator scanned a
    /// barcode only the post-reflow line picked up".
    /// </para>
    /// <para>
    /// <see cref="OperationCanceledException"/> is *not* caught —
    /// cancellation propagates so the request pipeline can abort
    /// cleanly.
    /// </para>
    /// <para>
    /// The returned <see cref="BoardTrace"/> is <c>null</c> only
    /// when zero sources are configured; a barcode that matched no
    /// stage is signalled by every stage having an empty
    /// <see cref="BoardStageTrace.Sides"/> list and no error — the
    /// endpoint layer maps that to 404.
    /// </para>
    /// </remarks>
    public static Task<BoardTrace?> GetBoardByBarcodeAsync(
        IEnumerable<IAoiSource> sources,
        string barcode,
        CancellationToken ct)
        => GetBoardByBarcodeAsync(sources, barcode, selectedPanelIds: null, ct);

    /// <summary>
    /// TC2 — cross-DB board trace by barcode with optional per-source
    /// pass overrides. <paramref name="selectedPanelIds"/> maps
    /// source id → panel id (one pin per source, last-wins at the
    /// endpoint). A pin that cannot be honoured falls back to the
    /// latest pass and sets <see cref="BoardStageTrace.SelectionWarning"/>.
    /// </summary>
    public static async Task<BoardTrace?> GetBoardByBarcodeAsync(
        IEnumerable<IAoiSource> sources,
        string barcode,
        IReadOnlyDictionary<string, int>? selectedPanelIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        var stages = new List<BoardStageTrace>();
        foreach (var source in sources)
        {
            int? pinnedId = null;
            if (selectedPanelIds is not null
                && selectedPanelIds.TryGetValue(source.Descriptor.Id, out var id))
            {
                pinnedId = id;
            }

            stages.Add(await ProbeStageAsync(source, barcode, pinnedId, ct).ConfigureAwait(false));
        }

        if (stages.Count == 0)
        {
            return null;
        }
        return new BoardTrace(barcode, stages);
    }

    private static async Task<BoardStageTrace> ProbeStageAsync(
        IAoiSource source,
        string barcode,
        int? pinnedPanelId,
        CancellationToken ct)
    {
        var descriptor = source.Descriptor;
        var pinsAvailable = source is IPinLevelSource;

        IReadOnlyList<PanelRow> panels;
        try
        {
            panels = await source
                .ListPanelsByBarcodeAsync(barcode, PriorPassLimit, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types — TC2 intentionally isolates per-stage failures.
        catch (Exception ex)
        {
            return new BoardStageTrace(
                SourceId: descriptor.Id,
                SourceName: descriptor.DisplayName,
                Capabilities: descriptor.Caps,
                Sides: Array.Empty<BoardStageSide>(),
                PinsAvailable: pinsAvailable,
                Error: ex.Message);
        }
#pragma warning restore CA1031

        if (panels.Count == 0)
        {
            return new BoardStageTrace(
                SourceId: descriptor.Id,
                SourceName: descriptor.DisplayName,
                Capabilities: descriptor.Caps,
                Sides: Array.Empty<BoardStageSide>(),
                PinsAvailable: pinsAvailable,
                Error: null);
        }

        // Group by face (newest-first within each face). The SQL /
        // fake already returns Face_Number, rn order; re-group so a
        // pin that lands on the wrong face is ignored for other faces.
        var byFace = panels
            .GroupBy(p => p.FaceNumber ?? 0)
            .OrderBy(g => g.Key)
            .ToList();

        string? selectionWarning = null;
        var honourPin = false;
        if (pinnedPanelId is int wanted)
        {
            var match = panels.FirstOrDefault(p => p.PanelId == wanted);
            if (match is null
                || !string.Equals(match.PanelBarCode, barcode, StringComparison.Ordinal))
            {
                selectionWarning =
                    "This pass link is older than the retained 10-pass window and is no longer available. Showing the latest pass.";
            }
            else
            {
                honourPin = true;
            }
        }

        // Sides typically share the same product / machine (both faces of
        // the same physical PCB inspected on the same AOI). Machine
        // names cache identically. Resolve the reference-data lists
        // once outside the loop so a two-sided board only pays the
        // cost of one machine / product / operator lookup instead of
        // two. Failures fall through as null (best-effort enrichment).
        IReadOnlyList<Product> products;
        try
        {
            products = await source.ListProductsAsync(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Best-effort enrichment; see per-resolver rationale below.
        catch (Exception) { products = Array.Empty<Product>(); }
#pragma warning restore CA1031
        IReadOnlyList<Machine> machines;
        try
        {
            machines = await source.ListMachinesAsync(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception) { machines = Array.Empty<Machine>(); }
#pragma warning restore CA1031
        IReadOnlyList<ReviewOperator> operators;
        try
        {
            operators = await source.ListOperatorsAsync(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception) { operators = Array.Empty<ReviewOperator>(); }
#pragma warning restore CA1031

        var sides = new List<BoardStageSide>(byFace.Count);
        foreach (var faceGroup in byFace)
        {
            var faceRows = faceGroup
                .OrderByDescending(p => p.PanelNumericDate)
                .ThenByDescending(p => p.PanelId)
                .ToList();

            PanelRow selected;
            int? pinnedForFace = null;
            if (honourPin
                && pinnedPanelId is int pin
                && faceRows.Exists(p => p.PanelId == pin))
            {
                selected = faceRows.First(p => p.PanelId == pin);
                pinnedForFace = pin;
            }
            else
            {
                selected = faceRows[0];
            }

            var productName = LookupProductName(products, selected.ProductId);
            var machineName = LookupMachineName(machines, selected.MachineId);
            var operatorName = LookupOperatorName(operators, selected.OperatorId);
            var productSvgKey = NormalizeProductSvgKey(productName);
            var materialised = Materialise(
                selected,
                productName,
                machineName,
                operatorName,
                productSvgKey);

            IReadOnlyList<CardRow> cards;
            try
            {
                cards = await source.ListCardsForPanelAsync(selected.PanelId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Per-stage error isolation: return what we have so far + the error text.
            catch (Exception ex)
            {
                return new BoardStageTrace(
                    SourceId: descriptor.Id,
                    SourceName: descriptor.DisplayName,
                    Capabilities: descriptor.Caps,
                    Sides: sides,
                    PinsAvailable: pinsAvailable,
                    Error: ex.Message);
            }
#pragma warning restore CA1031

            var prior = new List<PanelPassSummary>(faceRows.Count - 1);
            foreach (var row in faceRows)
            {
                if (row.PanelId == selected.PanelId)
                {
                    continue;
                }

                var (_, utc) = Split(row);
                prior.Add(new PanelPassSummary(
                    PanelId: row.PanelId,
                    FaceNumber: faceGroup.Key,
                    PanelUtc: utc,
                    PanelStatus: row.PanelStatus,
                    AnomalyBr: row.AnomalyBr,
                    AnomalyAr: row.AnomalyAr,
                    NbOfErrorObject: row.NbOfErrorObject,
                    HasBeenReviewed: row.HasBeenReviewed));
            }

            sides.Add(new BoardStageSide(
                FaceNumber: faceGroup.Key,
                Panel: materialised,
                Cards: cards,
                PriorPasses: prior,
                PinnedPanelId: pinnedForFace));
        }

        return new BoardStageTrace(
            SourceId: descriptor.Id,
            SourceName: descriptor.DisplayName,
            Capabilities: descriptor.Caps,
            Sides: sides,
            PinsAvailable: pinsAvailable,
            Error: null,
            SelectionWarning: selectionWarning);
    }

    private static TraceabilityPanel Materialise(
        PanelRow panel,
        string? productName = null,
        string? machineName = null,
        string? operatorName = null,
        string? productSvgKey = null)
    {
        var (_, panelUtc) = Split(panel);
        return new TraceabilityPanel(
            panel,
            panelUtc,
            productName,
            machineName,
            operatorName,
            productSvgKey);
    }

    /// <summary>
    /// Regex that trims the <c>_PreReflow</c> (or <c>-PreReflow</c>)
    /// suffix, with optional trailing whitespace, case-insensitively.
    /// The pre-reflow AOI programs are named e.g.
    /// <c>HA013682402_1st_PreReflow</c> while the SVG cache is keyed
    /// on the post-reflow product name (<c>HA013682402_1st</c>).
    /// Stripping the suffix lets both stages share one cached SVG.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PreReflowSuffix =
        new(@"[_\-]?PreReflow\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static string? NormalizeProductSvgKey(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }
        var trimmed = productName.Trim();
        var stripped = PreReflowSuffix.Replace(trimmed, "");
        // Belt-and-braces: never emit an empty key. If the whole
        // name was "PreReflow" or similar, fall back to the raw
        // trimmed input so the SVG endpoint gets something to try.
        return string.IsNullOrWhiteSpace(stripped) ? trimmed : stripped;
    }

    private static string? LookupProductName(IReadOnlyList<Product> products, int productId)
    {
        foreach (var p in products)
        {
            if (p.ProductId == productId)
            {
                return p.ProductName;
            }
        }
        return null;
    }

    private static string? LookupMachineName(IReadOnlyList<Machine> machines, int machineId)
    {
        foreach (var m in machines)
        {
            if (m.MachineId == machineId)
            {
                return m.MachineName;
            }
        }
        return null;
    }

    private static string? LookupOperatorName(IReadOnlyList<ReviewOperator> operators, int? operatorId)
    {
        if (operatorId is null)
        {
            return null;
        }
        foreach (var o in operators)
        {
            if (o.OperatorId == operatorId.Value)
            {
                return o.OperatorName;
            }
        }
        return null;
    }

    private static async Task<string?> ResolveProductNameAsync(
        IAoiSource source, int productId, CancellationToken ct)
    {
        try
        {
            var products = await source.ListProductsAsync(ct).ConfigureAwait(false);
            return LookupProductName(products, productId);
        }
#pragma warning disable CA1031 // Product-name enrichment is best-effort; failures fall back to id-only display.
        catch (Exception)
        {
        }
#pragma warning restore CA1031
        return null;
    }

    private static async Task<string?> ResolveMachineNameAsync(
        IAoiSource source, int machineId, CancellationToken ct)
    {
        try
        {
            var machines = await source.ListMachinesAsync(ct).ConfigureAwait(false);
            return LookupMachineName(machines, machineId);
        }
#pragma warning disable CA1031 // Machine-name enrichment is best-effort; failures fall back to id-only display.
        catch (Exception)
        {
        }
#pragma warning restore CA1031
        return null;
    }

    private static async Task<string?> ResolveOperatorNameAsync(
        IAoiSource source, int? operatorId, CancellationToken ct)
    {
        if (operatorId is null)
        {
            return null;
        }
        try
        {
            var operators = await source.ListOperatorsAsync(ct).ConfigureAwait(false);
            return LookupOperatorName(operators, operatorId);
        }
#pragma warning disable CA1031 // Operator-name enrichment is best-effort; failures fall back to id-only display.
        catch (Exception)
        {
        }
#pragma warning restore CA1031
        return null;
    }

    private static (PanelRow Panel, DateTime PanelUtc) Split(PanelRow panel)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(panel.PanelNumericDate).UtcDateTime;
        return (panel, utc);
    }
}
