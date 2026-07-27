import type { SourceInfo } from "../api/sources";

/**
 * URL-serialisable filter state for the skip-summary report. Every
 * field is URL-encoded via TanStack Router's search-params (see
 * router.ts::skipSummaryRoute.validateSearch) so a full report — source,
 * window, machine / product narrowing — can be shared, bookmarked, and
 * reloaded verbatim.
 */
export type SkipSummarySearch = {
    /** SourceDescriptor.Id, case-insensitive. */
    sourceId?: string;
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Panel machine ids. */
    machineIds?: number[];
    /** Panel product ids. */
    productIds?: number[];
    /**
     * Restrict to each panel's most recent inspection. The server
     * default is `true`, so the URL only carries this when the user
     * explicitly turns it off.
     */
    onlyLastInspection?: boolean;
};

/**
 * Serialise a {@link SkipSummarySearch} into the query-string shape the
 * API accepts (comma-separated id lists; no keys when empty). The
 * server defaults `onlyLastInspection` to `true`, so we only emit it
 * when the caller explicitly disabled it.
 */
export function toApiQuery(search: SkipSummarySearch): Record<string, string> {
    const out: Record<string, string> = {};
    if (search.sourceId) out.sourceId = search.sourceId;
    if (search.startUtc) out.startUtc = search.startUtc;
    if (search.endUtc) out.endUtc = search.endUtc;
    if (search.machineIds && search.machineIds.length > 0) {
        out.machineIds = search.machineIds.join(",");
    }
    if (search.productIds && search.productIds.length > 0) {
        out.productIds = search.productIds.join(",");
    }
    if (search.onlyLastInspection === false) {
        out.onlyLastInspection = "false";
    }
    return out;
}

/**
 * Validator for TanStack Router's `validateSearch`. Coerces raw URL
 * values into the typed {@link SkipSummarySearch} shape. Unknown keys
 * are dropped.
 */
export function validateSkipSummarySearch(raw: Record<string, unknown>): SkipSummarySearch {
    return {
        sourceId: toStringOrUndef(raw.sourceId),
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        machineIds: toNumberArray(raw.machineIds),
        productIds: toNumberArray(raw.productIds),
        onlyLastInspection: toBoolOrUndef(raw.onlyLastInspection),
    };
}

/**
 * Given the source-list response, pick a sensible default source id
 * when the URL does not specify one. Prefers the first available
 * source; falls back to the first source overall.
 */
export function pickDefaultSourceId(sources: readonly SourceInfo[]): string | undefined {
    if (sources.length === 0) return undefined;
    return (sources.find((s) => s.available) ?? sources[0]).id;
}

function toStringOrUndef(v: unknown): string | undefined {
    if (typeof v !== "string") return undefined;
    const trimmed = v.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}

function toBoolOrUndef(v: unknown): boolean | undefined {
    if (typeof v === "boolean") return v;
    if (typeof v === "string") {
        const s = v.trim().toLowerCase();
        if (s === "true") return true;
        if (s === "false") return false;
    }
    return undefined;
}

function toNumberArray(v: unknown): number[] | undefined {
    if (Array.isArray(v)) {
        const nums = v
            .map((x) => (typeof x === "string" || typeof x === "number" ? Number(x) : NaN))
            .filter((n) => Number.isFinite(n) && Number.isInteger(n));
        return nums.length > 0 ? nums : undefined;
    }
    if (typeof v === "string") {
        const nums = v
            .split(",")
            .map((s) => s.trim())
            .filter((s) => s.length > 0)
            .map((s) => Number(s))
            .filter((n) => Number.isFinite(n) && Number.isInteger(n));
        return nums.length > 0 ? nums : undefined;
    }
    return undefined;
}
