import {
    SKIP_EXCLUSIONS,
    SKIP_STATUS_VALUES,
    type SkipExclusion,
    type SkipStatus,
} from "./dpmo.search";

// Re-export the skip enums so the FPY-trend route imports them from one
// place (defined once in dpmo.search, shared across reports).
export { SKIP_EXCLUSIONS, SKIP_STATUS_VALUES };
export type { SkipExclusion, SkipStatus };

/** Time-bucket size. Matches the .NET `TimeBucket` names accepted by the API. */
export type FpyTrendBucketSize = "Week" | "Day";
export const FPY_TREND_BUCKETS: readonly FpyTrendBucketSize[] = ["Week", "Day"];

/** Panel- or sub-panel (board) level FPY. Matches `.NET FpyGranularity`. */
export type FpyTrendGranularity = "Board" | "Panel";
export const FPY_TREND_GRANULARITIES: readonly FpyTrendGranularity[] = ["Board", "Panel"];

/**
 * Which FPY flavour the chart/table highlight. Display-only: the API always
 * returns all three, so switching this never triggers a refetch.
 */
export type FpyTrendFlavor = "Diagnostic" | "Aoi";
export const FPY_TREND_FLAVORS: readonly FpyTrendFlavor[] = ["Diagnostic", "Aoi"];

/**
 * URL-serialisable filter state for the FPY-trend-by-line report. Every
 * field lives in the TanStack Router search-params so a full report — window,
 * bucket, granularity, flavour, skip mode — is shareable / bookmarkable /
 * reloadable verbatim.
 */
export type FpyTrendSearch = {
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Bucket size. Default `Week`. */
    bucket?: FpyTrendBucketSize;
    /** Panel vs sub-panel. Default `Board` (sub-panel). */
    granularity?: FpyTrendGranularity;
    /** Displayed FPY flavour. Default `Diagnostic`. Display-only (no refetch). */
    flavor?: FpyTrendFlavor;
    /** IANA/Windows time-zone id used to align day/week boundaries. Default UTC. */
    siteTimeZone?: string;
    /** Skip-exclusion mode. Default `Clean`. */
    skipExclusion?: SkipExclusion;
    /** Narrow to specific skip classes. */
    skipStatuses?: SkipStatus[];
    /** Restrict to specific production line numbers (parsed from machine names). */
    lines?: number[];
    /** Optional source-id scope; when empty the report runs across all sources. */
    sourceIds?: string[];
    /** Drop products whose name contains "NOGO". */
    excludeNogo?: boolean;
};

/**
 * Serialise a {@link FpyTrendSearch} into the query-string shape the API
 * accepts. NOTE: `flavor` is intentionally omitted — the JSON endpoint
 * returns every flavour, and the toggle is applied client-side. (The PDF
 * export URL adds it separately.)
 */
export function toApiQuery(search: FpyTrendSearch): Record<string, string> {
    const out: Record<string, string> = {};
    if (search.startUtc) out.startUtc = search.startUtc;
    if (search.endUtc) out.endUtc = search.endUtc;
    if (search.bucket) out.bucket = search.bucket;
    if (search.granularity) out.granularity = search.granularity;
    if (search.siteTimeZone) out.siteTimeZone = search.siteTimeZone;
    if (search.skipExclusion === "Clean") out.skipExclusion = "Clean";
    if (search.skipStatuses && search.skipStatuses.length > 0) {
        out.skipStatuses = search.skipStatuses.join(",");
    }
    if (search.lines && search.lines.length > 0) {
        out.lines = search.lines.join(",");
    }
    if (search.sourceIds && search.sourceIds.length > 0) {
        out.sourceIds = search.sourceIds.join(",");
    }
    if (search.excludeNogo) out.excludeNogo = "true";
    return out;
}

/**
 * Validator for TanStack Router's `validateSearch`. Coerces raw URL values
 * into the typed {@link FpyTrendSearch} shape, applying the report defaults
 * (Week / Board / Diagnostic / Clean).
 */
export function validateFpyTrendSearch(raw: Record<string, unknown>): FpyTrendSearch {
    return {
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        bucket: toEnumOrDefault<FpyTrendBucketSize>(raw.bucket, FPY_TREND_BUCKETS, "Week"),
        granularity: toEnumOrDefault<FpyTrendGranularity>(raw.granularity, FPY_TREND_GRANULARITIES, "Board"),
        flavor: toEnumOrDefault<FpyTrendFlavor>(raw.flavor, FPY_TREND_FLAVORS, "Diagnostic"),
        siteTimeZone: toStringOrUndef(raw.siteTimeZone),
        skipExclusion: toEnumOrDefault<SkipExclusion>(raw.skipExclusion, SKIP_EXCLUSIONS, "Clean"),
        skipStatuses: toEnumArray<SkipStatus>(raw.skipStatuses, SKIP_STATUS_VALUES),
        lines: toNumberArray(raw.lines),
        sourceIds: toStringArray(raw.sourceIds),
        excludeNogo: toBoolOrUndef(raw.excludeNogo),
    };
}

function toStringOrUndef(v: unknown): string | undefined {
    if (typeof v !== "string") return undefined;
    const trimmed = v.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}

function toBoolOrUndef(v: unknown): boolean | undefined {
    if (typeof v === "boolean") return v ? true : undefined;
    if (typeof v === "string") {
        const t = v.trim().toLowerCase();
        if (t === "true" || t === "1") return true;
    }
    return undefined;
}

function toEnumOrDefault<T extends string>(
    v: unknown,
    allowed: readonly T[],
    fallback: T,
): T {
    if (typeof v === "string") {
        const match = allowed.find((a) => a.toLowerCase() === v.trim().toLowerCase());
        if (match) return match;
    }
    return fallback;
}

function toStringArray(v: unknown): string[] | undefined {
    const parts = Array.isArray(v)
        ? v.filter((x): x is string => typeof x === "string")
        : typeof v === "string"
          ? v.split(",")
          : [];
    const cleaned = parts.map((s) => s.trim()).filter((s) => s.length > 0);
    return cleaned.length > 0 ? cleaned : undefined;
}

function toNumberArray(v: unknown): number[] | undefined {
    // Accept both a numeric array (produced by formToSearch during in-app
    // navigation, e.g. `lines: [2]`) and a comma-separated string (parsed from
    // the URL, e.g. `lines=2,7`). NOTE: do NOT route this through
    // `toStringArray`, whose `typeof x === "string"` filter silently drops
    // numbers — that made the Line filter vanish from the URL on submit.
    const parts: unknown[] = Array.isArray(v)
        ? v
        : typeof v === "string"
          ? v.split(",")
          : [];
    const out = parts
        .map((x) => (typeof x === "number" ? x : Number(String(x).trim())))
        .filter((n): n is number => Number.isFinite(n));
    return out.length > 0 ? out : undefined;
}

function toEnumArray<T extends string>(v: unknown, allowed: readonly T[]): T[] | undefined {
    const raw = toStringArray(v);
    if (!raw) return undefined;
    const out = raw
        .map((s) => allowed.find((a) => a.toLowerCase() === s.toLowerCase()))
        .filter((x): x is T => x !== undefined);
    return out.length > 0 ? out : undefined;
}
