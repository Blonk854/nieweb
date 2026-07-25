/**
 * Filter state for the TC3 board-lookup route
 * (`/traceability/board?barcode=X&side=1`). The barcode plus the
 * chosen physical PCB side are the only search parameters, so the
 * filter shape doubles as the saved-view payload.
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
};

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

