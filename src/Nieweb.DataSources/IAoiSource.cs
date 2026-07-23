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
    /// Returns AOI/inspection machines only
    /// (Superviseur <c>MACHINE.Machine_Type = 1</c>). Review stations
    /// (<c>Machine_Type = 2</c>, sometimes called "repair PCs") are
    /// excluded because they never appear as producers of
    /// <c>PANELS</c>/<c>CARDS</c> rows and would only pollute the
    /// filter dropdown and the admin Production Lines picker.
    /// </summary>
    Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct);

    Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct);

    Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct);

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
