/**
 * Filter state for the TC3 board-lookup route
 * (`/traceability/board?barcode=X&side=1&passes[postreflow]=123`).
 * The barcode, chosen physical PCB side, and optional per-source
 * pass pins are the search parameters; barcode + side form the
 * durable saved-view payload (pins are stripped on save).
 *
 * Kept as an object rather than a bare string so the saved-view menu
 * (which requires a JSON-serialisable filter) works uniformly with
 * this route just like it does for panel-yield / pareto.
 */
export type TraceabilityBoardSearch = {
    /** Panel barcode. Case-preserving; server-side comparison is exact. */
    barcode?: string;
    /**
     * Which physical side of the PCB to display
     * (<code>PANELS.Face_Number</code>). Both sides carry the same
     * laser-etched barcode so a scan returns two panel rows per
     * stage; the toggle above the stage cards flips between them.
     * When omitted the route auto-picks the first side that came
     * back (typically <code>1</code>).
     */
    side?: number;
    /**
     * Optional per-source pass overrides (sourceId → panelId).
     * Serialised to the API as repeated <code>panelId=src:id</code>.
     * One pin per source; side toggle keeps the pin in the URL.
     */
    passes?: Record<string, number>;
};

/**
 * Durable saved-view shape: barcode + side only. Historical pins age
 * out of the 10-pass window and must not be persisted.
 */
export function toSavedTraceabilityBoardFilter(
    search: TraceabilityBoardSearch,
): Pick<TraceabilityBoardSearch, "barcode" | "side"> {
    const out: Pick<TraceabilityBoardSearch, "barcode" | "side"> = {};
    if (search.barcode !== undefined) out.barcode = search.barcode;
    if (search.side !== undefined) out.side = search.side;
    return out;
}

/**
 * Validator for TanStack Router's `validateSearch`. Coerces raw URL
 * values into the typed `TraceabilityBoardSearch` shape. Unknown keys
 * are dropped. Trims whitespace and drops empty strings so
 * `?barcode=` behaves the same as no query param at all.
 *
 * Enforces the server-side 64-character cap client-side too: an
 * over-long barcode is silently dropped, which surfaces as the
 * "enter a barcode" empty state rather than a fetch that would 400
 * anyway.
 */
export function validateTraceabilityBoardSearch(
    raw: Record<string, unknown>,
): TraceabilityBoardSearch {
    return {
        barcode: toBarcodeOrUndef(raw.barcode),
        side: toSideOrUndef(raw.side),
        passes: toPassesOrUndef(raw.passes),
    };
}

function toBarcodeOrUndef(v: unknown): string | undefined {
    if (typeof v !== "string") return undefined;
    const trimmed = v.trim();
    if (trimmed.length === 0) return undefined;
    if (trimmed.length > 64) return undefined;
    return trimmed;
}

function toSideOrUndef(v: unknown): number | undefined {
    // Accept numeric or numeric string; anything else is dropped so
    // a stale bookmark can't crash the route.
    if (typeof v === "number" && Number.isFinite(v) && v > 0) {
        return Math.floor(v);
    }
    if (typeof v === "string") {
        const n = Number.parseInt(v, 10);
        if (Number.isFinite(n) && n > 0) return n;
    }
    return undefined;
}

function toPassesOrUndef(v: unknown): Record<string, number> | undefined {
    if (v == null) return undefined;

    // Compact form from some routers / bookmarks: "postreflow:1234"
    // or an array of such strings.
    if (typeof v === "string") {
        return parsePassEntry(v);
    }
    if (Array.isArray(v)) {
        const out: Record<string, number> = {};
        for (const item of v) {
            if (typeof item !== "string") continue;
            const one = parsePassEntry(item);
            if (one) Object.assign(out, one);
        }
        return Object.keys(out).length > 0 ? out : undefined;
    }

    if (typeof v !== "object") return undefined;
    const out: Record<string, number> = {};
    for (const [key, rawVal] of Object.entries(v as Record<string, unknown>)) {
        if (key.length === 0 || key.length > 32) continue;
        let n: number | undefined;
        if (typeof rawVal === "number" && Number.isFinite(rawVal) && rawVal > 0) {
            n = Math.floor(rawVal);
        } else if (typeof rawVal === "string") {
            const parsed = Number.parseInt(rawVal, 10);
            if (Number.isFinite(parsed) && parsed > 0) n = parsed;
        }
        if (n !== undefined) out[key] = n;
    }
    return Object.keys(out).length > 0 ? out : undefined;
}

function parsePassEntry(raw: string): Record<string, number> | undefined {
    const colon = raw.indexOf(":");
    if (colon <= 0 || colon >= raw.length - 1) return undefined;
    const sourceId = raw.slice(0, colon).trim();
    const idText = raw.slice(colon + 1).trim();
    if (sourceId.length === 0 || sourceId.length > 32) return undefined;
    const n = Number.parseInt(idText, 10);
    if (!Number.isFinite(n) || n <= 0) return undefined;
    return { [sourceId]: n };
}
