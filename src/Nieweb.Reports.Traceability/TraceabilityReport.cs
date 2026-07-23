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
    /// stage is signalled by every stage having
    /// <see cref="BoardStageTrace.Panel"/> = <c>null</c> and no
    /// error — the endpoint layer maps that to 404.
    /// </para>
    /// </remarks>
    public static async Task<BoardTrace?> GetBoardByBarcodeAsync(
        IEnumerable<IAoiSource> sources,
        string barcode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        var stages = new List<BoardStageTrace>();
        foreach (var source in sources)
        {
            stages.Add(await ProbeStageAsync(source, barcode, ct).ConfigureAwait(false));
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
        CancellationToken ct)
    {
        var descriptor = source.Descriptor;
        var pinsAvailable = source is IPinLevelSource;

        PanelRow? panel;
        try
        {
            panel = await source.GetPanelByBarcodeAsync(barcode, ct).ConfigureAwait(false);
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
                Panel: null,
                Cards: [],
                PinsAvailable: pinsAvailable,
                Error: ex.Message);
        }
#pragma warning restore CA1031

        if (panel is null)
        {
            return new BoardStageTrace(
                SourceId: descriptor.Id,
                SourceName: descriptor.DisplayName,
                Capabilities: descriptor.Caps,
                Panel: null,
                Cards: [],
                PinsAvailable: pinsAvailable,
                Error: null);
        }

        IReadOnlyList<CardRow> cards;
        try
        {
            cards = await source.ListCardsForPanelAsync(panel.PanelId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // See above — per-stage error isolation.
        catch (Exception ex)
        {
            return new BoardStageTrace(
                SourceId: descriptor.Id,
                SourceName: descriptor.DisplayName,
                Capabilities: descriptor.Caps,
                Panel: Materialise(panel, await ResolveProductNameAsync(source, panel.ProductId, ct).ConfigureAwait(false)),
                Cards: [],
                PinsAvailable: pinsAvailable,
                Error: ex.Message);
        }
#pragma warning restore CA1031

        return new BoardStageTrace(
            SourceId: descriptor.Id,
            SourceName: descriptor.DisplayName,
            Capabilities: descriptor.Caps,
            Panel: Materialise(panel, await ResolveProductNameAsync(source, panel.ProductId, ct).ConfigureAwait(false)),
            Cards: cards,
            PinsAvailable: pinsAvailable,
            Error: null);
    }

    private static TraceabilityPanel Materialise(PanelRow panel, string? productName = null)
    {
        var (_, panelUtc) = Split(panel);
        return new TraceabilityPanel(panel, panelUtc, productName);
    }

    private static async Task<string?> ResolveProductNameAsync(
        IAoiSource source, int productId, CancellationToken ct)
    {
        try
        {
            var products = await source.ListProductsAsync(ct).ConfigureAwait(false);
            foreach (var p in products)
            {
                if (p.ProductId == productId)
                {
                    return p.ProductName;
                }
            }
        }
#pragma warning disable CA1031 // Product-name enrichment is best-effort; failures fall back to id-only display.
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
