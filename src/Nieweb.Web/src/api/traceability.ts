import { apiFetch } from "./client";

/**
 * Typed clients + DTO mirrors for the `/api/traceability` endpoints
 * (TC1 + TC2). Field names follow the server's System.Text.Json
 * camelCase output. Records match:
 *   - `Nieweb.DataSources.PanelRow`
 *   - `Nieweb.DataSources.CardRow`
 *   - `Nieweb.DataSources.TestedObjectRow`
 *   - `Nieweb.DataSources.PinRow`
 *   - `Nieweb.Reports.Traceability.TraceabilityPanel`
 *   - `Nieweb.Reports.Traceability.TraceabilitySubpanel`
 *   - `Nieweb.Reports.Traceability.TraceabilityTestedObject`
 *   - `Nieweb.Reports.Traceability.BoardStageTrace`
 *   - `Nieweb.Reports.Traceability.BoardTrace`
 *   - `Nieweb.Api.Endpoints.SubpanelsResponse`
 *   - `Nieweb.Api.Endpoints.TestedObjectsResponse`
 */

/** Mirrors `Nieweb.DataSources.PanelRow`. */
export type PanelRow = {
    panelId: number;
    machineId: number;
    laneNumber: number;
    panelBarCode: string;
    panelNumericDate: number;
    nbOfValidCards: number;
    testTime: number;
    panelStatus: number;
    anomalyBr: number;
    anomalyAr: number;
    hasBeenReviewed: boolean;
    nbOfTestedObject: number;
    nbOfErrorObject: number;
    operatorId: number | null;
    productId: number;
    recipeId: number;
    /**
     * `PANELS.Face_Number` — which side of the physical PCB this
     * inspection ran on. Both live DBs ship this NOT NULL, so a
     * <code>null</code> here signals "source's schema omits it".
     * TC2 board trace splits a barcode into per-side sub-cards on
     * this field.
     */
    faceNumber: number | null;
};

/** Mirrors `Nieweb.DataSources.CardRow`. */
export type CardRow = {
    panelId: number;
    cardIdOnPanel: number;
    cardStatus: number;
    anomalyBr: number;
    anomalyAr: number;
    nbOfTestedObject: number;
    nbOfErrorObject: number;
    machineId: number;
    productId: number;
    panelNumericDate: number;
};

/** Mirrors `Nieweb.DataSources.TestedObjectRow`. */
export type TestedObjectRow = {
    panelId: number;
    cardIdOnPanel: number;
    objectId: number;
    objectTypeId: number;
    errorTable: number;
    errorTableAr: number;
    status: number;
    machineId: number;
    productId: number;
    panelNumericDate: number;
    topology: string | null;
    partNumberName: string | null;
    jedecName: string | null;
    deltaXUm: number | null;
    deltaYUm: number | null;
    deltaThetaDeg: number | null;
    deltaThicknessUm: number | null;
    deltaSurface: number | null;
    /**
     * TC5 Phase B — panel-side name from `PANELS.Face` (e.g. "Top",
     * "Bottom"). Inherited from the parent panel because the AOI DB
     * stores the side at panel granularity, not per-component.
     */
    face: string | null;
    /** TC5 Phase B — numeric side code from `PANELS.Face_Number`. */
    faceNumber: number | null;
    /**
     * TC5 Phase B — feeder identifier from `FEEDER.Feeder_Machine`
     * joined via `TESTED_OBJECT.Feeder_Id`. Rendered verbatim in the
     * failed-objects table.
     */
    feederName: string | null;
    /**
     * TC5 Phase B — `TESTED_OBJECT.Repair_State_Result`
     * (-2..3 enum; see the `vit-aoi-database` skill).
     */
    repairState: number | null;
    /**
     * TC5 Phase B — `TESTED_OBJECT.Repair_Numeric_Date_Hour` ANSI
     * `time_t` (seconds since 1970-01-01 UTC). Convert with
     * `new Date(repairUtc * 1000)`.
     */
    repairUtc: number | null;
    /** TC5 Phase B — `TESTED_OBJECT.Repair_Button_Comment`. */
    repairButtonComment: string | null;
    /** TC5 Phase B — `TESTED_OBJECT.Repair_Error_Comment`. */
    repairErrorComment: string | null;
    /** TC5 Phase B — `TESTED_OBJECT.Repair_Operator_Comments`. */
    repairOperatorComment: string | null;
    /** TC5 Phase B — `TESTED_OBJECT.Operator_Id`. */
    repairOperatorId: number | null;
};

/** Mirrors `Nieweb.DataSources.PinRow`. */
export type PinRow = {
    pinId: number;
    testedObjectId: number;
    componentSide: number;
    pinIndexOnSide: number;
    ipcPinNb: number | null;
    errorTable: number;
    errorTableAr: number;
    reviewSanction: number;
};

/**
 * Mirrors `Nieweb.DataSources.Capabilities` (flags enum, System.Text.Json
 * serialises as a plain number).
 */
export type Capabilities = number;

/** Mirrors `Nieweb.Reports.Traceability.TraceabilityPanel`. */
export type TraceabilityPanel = {
    panel: PanelRow;
    /** ISO-8601 UTC — server converts `Panel_Numeric_Date` for us. */
    panelUtc: string;
    /** Human-readable product name (resolved via `IAoiSource.ListProductsAsync`), or `null` when unresolved. */
    productName: string | null;
    /**
     * Human-readable AOI machine name (resolved via
     * `IAoiSource.ListMachinesAsync`), or `null` when unresolved. Only
     * populated by TC2 (Board trace).
     */
    machineName: string | null;
    /**
     * Review-operator name (resolved via
     * `IAoiSource.ListOperatorsAsync` against `panel.operatorId`), or
     * `null` when the panel carries no operator id / resolution failed.
     * Only populated by TC2 (Board trace).
     */
    operatorName: string | null;
    /**
     * Normalised product name suitable for the SVG cache lookup
     * (<code>GET /api/board-svgs/{key}</code>). Strips the
     * <code>_PreReflow</code> suffix so pre- and post-reflow
     * panels for the same physical PCB resolve to the same SVG.
     * Falls back to `productName` on older payloads.
     */
    productSvgKey: string | null;
};

/** Mirrors `Nieweb.Reports.Traceability.TraceabilitySubpanel`. */
export type TraceabilitySubpanel = {
    panel: PanelRow;
    panelUtc: string;
    card: CardRow;
};

/** Mirrors `Nieweb.Reports.Traceability.TraceabilityTestedObject`. */
export type TraceabilityTestedObject = {
    panel: PanelRow;
    panelUtc: string;
    card: CardRow;
    testedObject: TestedObjectRow;
    pins: PinRow[];
    pinsAvailable: boolean;
};

/**
 * Mirrors `Nieweb.Reports.Traceability.BoardStageSide`. One entry
 * per inspected side of the physical PCB on a single AOI source.
 */
export type BoardStageSide = {
    faceNumber: number;
    panel: TraceabilityPanel;
    cards: CardRow[];
};

/** Mirrors `Nieweb.Reports.Traceability.BoardStageTrace`. */
export type BoardStageTrace = {
    sourceId: string;
    sourceName: string;
    capabilities: Capabilities;
    /**
     * One entry per inspected side of the physical PCB on this
     * source, sorted by `faceNumber` ascending. Empty when the
     * barcode was never seen here.
     */
    sides: BoardStageSide[];
    pinsAvailable: boolean;
    error: string | null;
};

/** Mirrors `Nieweb.Reports.Traceability.BoardTrace`. */
export type BoardTrace = {
    barcode: string;
    stages: BoardStageTrace[];
};

/** Mirrors `Nieweb.Api.Endpoints.SubpanelsResponse`. */
export type SubpanelsResponse = {
    panel: TraceabilityPanel;
    cards: CardRow[];
};

/** Mirrors `Nieweb.Api.Endpoints.TestedObjectsResponse`. */
export type TestedObjectsResponse = {
    subpanel: TraceabilitySubpanel;
    objects: TestedObjectRow[];
};

/**
 * TC5 Phase C — mirrors `Nieweb.Api.Endpoints.FailedObjectsResponse`.
 * Carries the panel breadcrumb plus every failing tested object
 * across all sub-panels, ordered by `cardIdOnPanel` then `objectId`.
 * Filter semantics: `errorTableAr !== 0` (post-review defects only —
 * false calls cleared during review do not appear).
 */
export type FailedObjectsResponse = {
    panel: TraceabilityPanel;
    objects: TestedObjectRow[];
};

/**
 * TC2 board lookup by barcode. Fans across every configured source
 * and returns a stable, per-source stage list. Throws `ApiError` on
 * 400 (missing/oversized barcode) and 404 (barcode seen on no stage
 * and no error) — matches server contract in
 * TraceabilityEndpoints.GetBoardByBarcodeAsync.
 */
export function fetchBoardByBarcode(barcode: string): Promise<BoardTrace> {
    const q = new URLSearchParams({ barcode });
    return apiFetch<BoardTrace>(`/api/traceability/boards/by-barcode?${q.toString()}`);
}

/** TC1 subpanel list. */
export function fetchSubpanelsForPanel(
    sourceId: string,
    panelId: number,
): Promise<SubpanelsResponse> {
    return apiFetch<SubpanelsResponse>(
        `/api/traceability/panels/${encodeURIComponent(sourceId)}/${panelId}/subpanels`,
    );
}

/** TC1 tested-objects list for a subpanel. */
export function fetchTestedObjectsForSubpanel(
    sourceId: string,
    panelId: number,
    cardId: number,
): Promise<TestedObjectsResponse> {
    return apiFetch<TestedObjectsResponse>(
        `/api/traceability/panels/${encodeURIComponent(sourceId)}/${panelId}/subpanels/${cardId}/objects`,
    );
}

/** TC1 tested-object + pins detail. */
export function fetchTestedObject(
    sourceId: string,
    panelId: number,
    cardId: number,
    objectId: number,
): Promise<TraceabilityTestedObject> {
    return apiFetch<TraceabilityTestedObject>(
        `/api/traceability/panels/${encodeURIComponent(sourceId)}/${panelId}/subpanels/${cardId}/objects/${objectId}`,
    );
}

/**
 * TC5 Phase C — fetches every failed tested object for a panel
 * across all sub-panels, on the given stage source. The server
 * filters to `errorTableAr !== 0` so only post-review defects
 * appear (false calls cleared during review are excluded). Returns
 * an empty `objects` array when the panel exists but has no
 * failures. Throws `ApiError(404)` when the panel is unknown to
 * the source.
 */
export function fetchFailedObjectsForPanel(
    sourceId: string,
    panelId: number,
): Promise<FailedObjectsResponse> {
    return apiFetch<FailedObjectsResponse>(
        `/api/traceability/panels/${encodeURIComponent(sourceId)}/${panelId}/failed-objects`,
    );
}
