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

    Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct);

    Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct);

    Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct);

    /// <summary>
    /// Returns the wall-clock UTC timestamp of the most recent PANELS row, or
    /// <c>null</c> if the table is empty. Useful for UI freshness indicators
    /// and for sizing default query windows relative to the source's own data.
    /// </summary>
    Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct);
}

/// <summary>Optional: source exposes PIN + PIN_MEASURE tables (post-reflow v5.0 only).</summary>
public interface IPinLevelSource
{
    // Method signatures deliberately omitted for now; will be filled once we
    // sketch the Cp/Cpk + measure workflows that actually consume pin data.
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
