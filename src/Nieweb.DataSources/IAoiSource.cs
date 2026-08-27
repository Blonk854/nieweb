namespace Nieweb.DataSources;

/// <summary>
/// Universal contract every AOI Superviseur data source must satisfy. Covers
/// the tables that exist in both v4.3.1 (pre-reflow) and v5.0 (post-reflow):
/// PANELS, CARDS, TESTED_OBJECT, MACHINE, PRODUCT, RECIPE.
///
/// Features that are only in one schema version are exposed via segregated
/// optional interfaces (e.g. <see cref="IPinLevelSource"/>). Consumers should
/// pattern-match / type-check on those before invoking them.
/// </summary>
public interface IAoiSource
{
    SourceDescriptor Descriptor { get; }

    Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct);

    Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct);

    Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct);

    /// <summary>Stream all matching PANELS rows for exports. No paging.</summary>
    IAsyncEnumerable<PanelRow> StreamPanelsAsync(PanelQuery query, CancellationToken ct);

    /// <summary>
    /// Stream all matching CARDS rows for board-level aggregations
    /// (e.g. board-flavour FPY / DPMO). Rows carry <c>MachineId</c> and
    /// <c>ProductId</c> from the parent panel so the report layer can
    /// group without an additional round-trip.
    /// </summary>
    IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery query, CancellationToken ct);

    /// <summary>
    /// Stream all matching TESTED_OBJECT rows for component-level
    /// aggregations (DPMO table, Pareto chart, deviation trend). Rows
    /// carry <c>MachineId</c>, <c>ProductId</c>, and
    /// <c>PanelNumericDate</c> from the parent panel plus reference-data
    /// strings (<c>Topology</c>, <c>PartNumberName</c>, <c>JedecName</c>)
    /// so the report layer can group without extra queries.
    /// </summary>
    IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct);

    /// <summary>
    /// Returns Vision AOI machines only
    /// (Superviseur <c>MACHINE.Machine_Type = 1</c>). Review stations
    /// (<c>Machine_Type = 2</c>) are excluded because they never
    /// appear as producers of <c>PANELS</c>/<c>CARDS</c> rows and
    /// would only pollute the filter dropdown and the admin
    /// Production Lines picker.
    /// </summary>
    Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct);

    /// <summary>
    /// Returns all review-station operators known to the Superviseur
    /// (rows of <c>dbo.OPERATOR</c>). Small table (a few hundred rows at
    /// most on either live DB) so callers may safely cache the result for
    /// the lifetime of a request. Used to resolve
    /// <c>PANELS.Operator_Id</c> and <c>TESTED_OBJECT.Operator_Id</c>
    /// (both are the review operator) into a human-readable name.
    /// </summary>
    Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct);

    Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct);

    Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct);

    /// <summary>
    /// Returns the distinct <c>(Machine_Id, Product_Id)</c> pairs that
    /// produced at least one <c>PANELS</c> row inside
    /// <paramref name="window"/>. Powers the cascading filter dropdowns:
    /// the UI derives "machines that ran in the window" and "products that
    /// ran in the window on the selected machine(s)" from this single set,
    /// so it only ever offers combinations that actually ran.
    /// </summary>
    /// <remarks>
    /// The default implementation derives the set by streaming
    /// <see cref="StreamPanelsAsync"/> (fine for in-memory fakes). SQL
    /// adapters override it with a single windowed
    /// <c>SELECT DISTINCT Machine_Id, Product_Id</c> so the production DB
    /// does one cheap indexed scan instead of returning every panel row.
    /// </remarks>
    async Task<IReadOnlyList<ActivePanelKey>> ListActivePanelKeysAsync(
        DateRange window,
        CancellationToken ct)
    {
        var seen = new HashSet<ActivePanelKey>();
        var result = new List<ActivePanelKey>();
        var query = new PanelQuery { Window = window, OnlyLastInspection = false };
        await foreach (var panel in StreamPanelsAsync(query, ct).ConfigureAwait(false))
        {
            var key = new ActivePanelKey(panel.MachineId, panel.ProductId);
            if (seen.Add(key))
            {
                result.Add(key);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the wall-clock UTC timestamp of the most recent PANELS row, or
    /// <c>null</c> if the table is empty. Useful for UI freshness indicators
    /// and for sizing default query windows relative to the source's own data.
    /// </summary>
    Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct);

    /// <summary>
    /// Looks up a single panel by its <c>PANELS.Panel_Id</c>. Returns
    /// <c>null</c> when no panel with that id exists. Used by the
    /// traceability drill-down (TC1) so the API can resolve a
    /// panel-detail request without pulling a full time window.
    /// </summary>
    /// <remarks>
    /// Read-only, no window filter (a specific panel id is precise).
    /// Adapters must still use <c>WITH (NOLOCK)</c> and the shared
    /// isolation-prelude discipline documented on
    /// <c>SqlServerAoiSourceBase</c>.
    /// </remarks>
    Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct);

    /// <summary>
    /// Looks up the most recent panel that matches
    /// <paramref name="barcode"/> on <c>PANELS.Panel_Bar_Code</c>.
    /// A single physical PCB can be inspected multiple times (each
    /// crossing yields a new panel row); this method returns the row
    /// with the largest <c>Panel_Numeric_Date</c>. Returns <c>null</c>
    /// when no matching panel exists.
    /// </summary>
    /// <remarks>
    /// Entry point for TC3's panel-barcode search box. Not implemented
    /// via <c>Barcode_Product</c> (which only exists post-reflow) —
    /// direct <c>Panel_Bar_Code</c> equality works on both DBs.
    /// </remarks>
    Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct);

    /// <summary>
    /// Looks up every side of the physical PCB that matches
    /// <paramref name="barcode"/> on <c>PANELS.Panel_Bar_Code</c>.
    /// A two-sided board that carries the same laser-etched serial
    /// on both sides yields two rows here (one per
    /// <c>Face_Number</c>). When a side has been inspected multiple
    /// times, only the most recent inspection is returned. Rows are
    /// ordered by <c>Face_Number</c> ascending so callers can
    /// render side 1 before side 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entry point for TC2 board trace, which needs both sides so
    /// operators can flip between them. Forwards to
    /// <see cref="ListPanelsByBarcodeAsync(string, int, CancellationToken)"/>
    /// with <c>limit: 1</c> (latest pass per face).
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(
        string barcode,
        CancellationToken ct)
        => ListPanelsByBarcodeAsync(barcode, limit: 1, ct);

    /// <summary>
    /// Looks up up to <paramref name="limit"/> most-recent inspections
    /// per <c>Face_Number</c> for <paramref name="barcode"/>. Rows are
    /// ordered by <c>Face_Number</c> ascending, then newest-first within
    /// each face (<c>Panel_Numeric_Date DESC, Panel_Id DESC</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by TC2 board trace when surfacing prior AOI passes. The
    /// default implementation wraps
    /// <see cref="GetPanelByBarcodeAsync"/> and therefore returns at
    /// most one row regardless of <paramref name="limit"/> — SQL
    /// adapters and multi-pass fakes must override this overload.
    /// </para>
    /// </remarks>
    async Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(
        string barcode,
        int limit,
        CancellationToken ct)
    {
        _ = limit;
        var panel = await GetPanelByBarcodeAsync(barcode, ct).ConfigureAwait(false);
        return panel is null ? Array.Empty<PanelRow>() : new[] { panel };
    }

    /// <summary>
    /// Lists all <c>CARDS</c> (sub-panels) for a specific panel. No
    /// time window (a specific panel id already scopes the read).
    /// Rows are ordered by <c>Card_Number</c> ascending so the
    /// caller can index them positionally.
    /// </summary>
    Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct);

    /// <summary>
    /// Lists all <c>TESTED_OBJECT</c> rows for a specific sub-panel
    /// (identified by parent panel id + within-panel card number).
    /// Rows are ordered by <c>Tested_Object_Id</c> ascending.
    /// </summary>
    Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
        long panelId, int cardIdOnPanel, CancellationToken ct);

    /// <summary>
    /// Lists every failed <c>TESTED_OBJECT</c> row for a specific
    /// panel, aggregated across all of its sub-panels (<c>CARDS</c>).
    /// A "failed" row is one whose <see cref="TestedObjectRow.ErrorTableAr"/>
    /// (post-review defect bitfield) is non-zero — this matches the
    /// TC5 failed-objects table which shows post-review defects, not
    /// raw pre-review AOI opinions. On sources that lack the AR
    /// column (pre-reflow v4.3.1) the adapter mirrors
    /// <c>Error_Table</c> into <c>Error_Table_AR</c>, so the same
    /// filter reads "actual defects" uniformly across schemas.
    /// Rows are ordered by <c>Card_Number</c>, then
    /// <c>Tested_Object_Id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default implementation fans out across
    /// <see cref="ListCardsForPanelAsync"/> and
    /// <see cref="ListTestedObjectsForSubpanelAsync"/>, skipping
    /// sub-panels whose <see cref="CardRow.NbOfErrorObject"/> is 0
    /// (they cannot contribute failing rows). Adapters that can
    /// answer this in a single round-trip should override for
    /// performance — <c>SqlServerAoiSourceBase</c> does exactly that.
    /// </para>
    /// <para>
    /// Entry point for the TC5 failed-objects tables. Never mutates,
    /// never spans the review-window — it is a point-lookup by panel
    /// id, just like the other TC1/TC5 helpers.
    /// </para>
    /// </remarks>
    async Task<IReadOnlyList<TestedObjectRow>> ListFailedTestedObjectsForPanelAsync(
        long panelId, CancellationToken ct)
    {
        var cards = await ListCardsForPanelAsync(panelId, ct).ConfigureAwait(false);
        var result = new List<TestedObjectRow>();
        foreach (var card in cards)
        {
            if (card.NbOfErrorObject <= 0)
            {
                // Sub-panel has no failures to contribute — skip the
                // round-trip. NbOfErrorObject is stored on CARDS and
                // maintained by the AOI machine when a panel finishes
                // inspection, so it is authoritative for this check.
                continue;
            }
            var objects = await ListTestedObjectsForSubpanelAsync(
                panelId, card.CardIdOnPanel, ct).ConfigureAwait(false);
            foreach (var o in objects)
            {
                if (o.ErrorTableAr != 0)
                {
                    result.Add(o);
                }
            }
        }
        return result;
    }
}

/// <summary>Optional: source exposes PIN + PIN_MEASURE tables (post-reflow v5.0 only).</summary>
public interface IPinLevelSource
{
    /// <summary>
    /// Lists every <c>PIN</c> row for the given tested-object
    /// (<c>TESTED_OBJECT.Tested_Object_Id</c>, which is the same
    /// value exposed on <see cref="TestedObjectRow.ObjectId"/>).
    /// Rows are ordered by <c>Component_Side</c> then
    /// <c>Pin_Index_On_Side</c> so the drill-down UI can render the
    /// pins in library-oriented perimeter order.
    /// </summary>
    /// <remarks>
    /// Only <c>PIN</c> columns are projected — this method does not
    /// join <c>PIN_MEASURE</c>. Cp/Cpk / measurement drill-down will
    /// land as a separate method on this interface (TC1 keeps the
    /// pin listing minimal to bound query cost on a live line DB).
    /// </remarks>
    Task<IReadOnlyList<PinRow>> ListPinsForObjectAsync(long testedObjectId, CancellationToken ct);
}

/// <summary>Optional: source exposes the *_HISTO review audit tables.</summary>
public interface IReviewAuditSource
{
    // TBD.
}

/// <summary>Optional: source exposes PastePads_* / Stencil_D* (pre-reflow v4.3.1 only).</summary>
public interface IPastePrintSource
{
    // TBD.
}

/// <summary>Optional: source has meaningful FEEDER data (pre-reflow only).</summary>
public interface IFeederAnalyticsSource
{
    // TBD.
}

/// <summary>Optional: source exposes the Barcode_Product view (post-reflow only).</summary>
public interface IBarcodeLookupSource
{
    // TBD.
}
