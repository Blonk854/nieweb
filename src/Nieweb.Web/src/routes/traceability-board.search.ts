/**
 * Filter state for the TC3 board-lookup route
 * (`/traceability/board?barcode=X`). The barcode is the only search
 * parameter, so the filter shape doubles as the saved-view payload.
 *
 * Kept as an object rather than a bare string so the saved-view menu
 * (which requires a JSON-serialisable filter) works uniformly with
 * this route just like it does for panel-yield / pareto.
 */
export type TraceabilityBoardSearch = {
    /** Panel barcode. Case-preserving; server-side comparison is exact. */
    barcode?: string;
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
    };
}

function toBarcodeOrUndef(v: unknown): string | undefined {
    if (typeof v !== "string") return undefined;
    const trimmed = v.trim();
    if (trimmed.length === 0) return undefined;
    if (trimmed.length > 64) return undefined;
    return trimmed;
}
