using Nieweb.DataSources;

namespace Nieweb.Reports.Traceability;

/// <summary>
/// Detail view of a single AOI <c>PANELS</c> row, returned by
/// <see cref="TraceabilityReport.GetPanelDetailAsync"/>. Exposes the
/// entire <see cref="PanelRow"/> plus a normalised UTC timestamp so
/// clients do not have to reason about <c>Panel_Numeric_Date</c>
/// (ANSI <c>time_t</c>).
/// </summary>
/// <param name="Panel">The raw panel row from <see cref="IAoiSource"/>.</param>
/// <param name="PanelUtc">
/// <see cref="PanelRow.PanelNumericDate"/> converted to UTC. The
/// underlying column is an <c>int</c> (seconds since 1970-01-01 UTC).
/// </param>
/// <param name="ProductName">Product name resolved via <see cref="IAoiSource.ListProductsAsync"/>, or <c>null</c> if unresolved.</param>
/// <param name="MachineName">
/// Vision AOI machine name resolved via <see cref="IAoiSource.ListMachinesAsync"/>,
/// or <c>null</c> if unresolved. Only populated by TC2
/// (<see cref="TraceabilityReport.GetBoardByBarcodeAsync"/>) so the
/// Board trace summary can render "Machine: L7PSTAOI" instead of a
/// raw numeric id.
/// </param>
/// <param name="OperatorName">
/// Review operator name resolved via <see cref="IAoiSource.ListOperatorsAsync"/>
/// against <see cref="PanelRow.OperatorId"/>, or <c>null</c> when the
/// panel carries no operator id or resolution failed. Only populated
/// by TC2 for the same reason as <see cref="MachineName"/>.
/// </param>
/// <param name="ProductSvgKey">
/// Normalised product name suitable for looking up the cached
/// panel SVG (<c>GET /api/board-svgs/{key}</c>). Strips the
/// <c>_PreReflow</c> / <c>-PreReflow</c> suffix that pre-reflow
/// products carry, so a pre-reflow panel and the corresponding
/// post-reflow panel resolve to the same SVG key (the same physical
/// PCB shares one board layout).
/// </param>
public sealed record TraceabilityPanel(
    PanelRow Panel,
    DateTime PanelUtc,
    string? ProductName,
    string? MachineName = null,
    string? OperatorName = null,
    string? ProductSvgKey = null);

/// <summary>
/// Detail view of a single <c>CARDS</c> (sub-panel) row, returned by
/// <see cref="TraceabilityReport.GetSubpanelDetailAsync"/>.
/// </summary>
/// <param name="Panel">The parent panel (repeated in every sub-panel response so callers can render breadcrumbs without a second call).</param>
/// <param name="PanelUtc">UTC of <see cref="PanelRow.PanelNumericDate"/>.</param>
/// <param name="Card">The raw card row.</param>
public sealed record TraceabilitySubpanel(
    PanelRow Panel,
    DateTime PanelUtc,
    CardRow Card);

/// <summary>
/// Detail view of a single <c>TESTED_OBJECT</c> row, returned by
/// <see cref="TraceabilityReport.GetTestedObjectDetailAsync"/>. Pin
/// data is only populated when the source implements
/// <see cref="IPinLevelSource"/> (post-reflow v5.0 only).
/// </summary>
/// <param name="Panel">The parent panel (breadcrumb).</param>
/// <param name="PanelUtc">UTC of <see cref="PanelRow.PanelNumericDate"/>.</param>
/// <param name="Card">The parent sub-panel (breadcrumb).</param>
/// <param name="TestedObject">The raw tested-object row.</param>
/// <param name="Pins">
/// The pins that belong to this tested object, ordered by
/// <c>Component_Side</c> then <c>Pin_Index_On_Side</c>. Empty when
/// the source lacks pin-level access (<c>PinsAvailable</c> is false).
/// </param>
/// <param name="PinsAvailable">
/// <c>true</c> when the source implements <see cref="IPinLevelSource"/>
/// and pin listing was attempted; <c>false</c> on pre-reflow sources
/// where the <c>PIN</c> table is absent. UI can render a
/// "Pin-level data not available on this source" hint.
/// </param>
public sealed record TraceabilityTestedObject(
    PanelRow Panel,
    DateTime PanelUtc,
    CardRow Card,
    TestedObjectRow TestedObject,
    IReadOnlyList<PinRow> Pins,
    bool PinsAvailable);

/// <summary>
/// One inspection side of a physical PCB on a single AOI source.
/// A two-sided board with barcode <c>XYZ</c> yields two
/// <see cref="BoardStageSide"/> entries per stage: face 1 and
/// face 2. Each side carries its own panel row + sub-panel list
/// because the AOI machine treats the two sides as separate
/// inspection cycles.
/// </summary>
/// <param name="FaceNumber">
/// <c>PANELS.Face_Number</c> from the underlying panel row. Used
/// as the discriminator for the SPA's side toggle (<c>1</c> = 1st
/// side, <c>2</c> = 2nd side). Sides are returned in ascending
/// order so the UI can render them left-to-right.
/// </param>
/// <param name="Panel">The panel row + resolved names for this side.</param>
/// <param name="Cards">Sub-panels attached to <see cref="Panel"/>.</param>
public sealed record BoardStageSide(
    int FaceNumber,
    TraceabilityPanel Panel,
    IReadOnlyList<CardRow> Cards);

/// <summary>
/// Per-source stage of a cross-DB board trace (TC2). One entry per
/// configured <see cref="IAoiSource"/>. When a barcode was only
/// captured by one stage (e.g. the pre-reflow scanner missed the
/// serial number) the other stage returns with an empty
/// <see cref="Sides"/> list and no error, so the SPA can render
/// one table per stage independently.
/// </summary>
/// <param name="SourceId">Descriptor id (e.g. <c>"postreflow"</c>).</param>
/// <param name="SourceName">Human-readable source name.</param>
/// <param name="Capabilities">Capability flags for this source — the SPA uses this to decide which columns to show (paste-print vs pin).</param>
/// <param name="Sides">
/// One entry per inspected side of the physical PCB on this
/// source, sorted by <see cref="BoardStageSide.FaceNumber"/>
/// ascending. Empty when the barcode was never seen here.
/// </param>
/// <param name="PinsAvailable">
/// <c>true</c> when this source implements
/// <see cref="IPinLevelSource"/>. The board trace itself never
/// carries pin rows (TC2 keeps the response small); callers drill
/// into pins via the TC1 tested-object endpoint.
/// </param>
/// <param name="Error">
/// Populated when the source threw while resolving the barcode. The
/// other stages still return normally so a single-DB outage never
/// crashes the whole payload.
/// </param>
public sealed record BoardStageTrace(
    string SourceId,
    string SourceName,
    Capabilities Capabilities,
    IReadOnlyList<BoardStageSide> Sides,
    bool PinsAvailable,
    string? Error);

/// <summary>
/// Cross-DB board trace (TC2). Returned by
/// <see cref="TraceabilityReport.GetBoardByBarcodeAsync"/>. Contains
/// one <see cref="BoardStageTrace"/> per configured source so the
/// SPA can render side-by-side tables — one per DB stage.
/// </summary>
/// <param name="Barcode">The barcode that was looked up (echoed verbatim).</param>
/// <param name="Stages">
/// One entry per source, in the order the sources were supplied
/// (typically descriptor-id order, e.g. <c>postreflow</c> then
/// <c>prereflow</c>). Every entry is present even when its
/// <c>Panel</c> is <c>null</c>, so clients can render a fixed
/// column layout.
/// </param>
public sealed record BoardTrace(
    string Barcode,
    IReadOnlyList<BoardStageTrace> Stages);
