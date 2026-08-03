import {
    DPMO_NUMERATORS,
    SKIP_EXCLUSIONS,
    SKIP_STATUS_VALUES,
    type DpmoNumerator,
    type DpmoOpportunity,
    type SkipExclusion,
    type SkipStatus,
} from "./dpmo.search";

// Re-export the shared enums so the DPMO-trend route imports them from one
// place (defined once in dpmo.search, shared across reports).
export { DPMO_NUMERATORS, SKIP_EXCLUSIONS, SKIP_STATUS_VALUES };
export type { DpmoNumerator, DpmoOpportunity, SkipExclusion, SkipStatus };

/** Time-bucket size. Matches the .NET `TimeBucket` names accepted by the API. */
export type DpmoTrendBucketSize = "Week" | "Day";
export const DPMO_TREND_BUCKETS: readonly DpmoTrendBucketSize[] = ["Week", "Day"];

/**
 * Opportunity flavours offered by the trend UI.
 *
 * `Paste` is deliberately absent. Paste opportunities come from
 * `CARDS.Nb_Of_Tests_On_Pads`, which only exists on sources advertising
 * `PastePrintMetrics` — i.e. pre-reflow only, because paste printing is a
 * pre-reflow stage. A paste trend would therefore render an empty series for
 * post-reflow on every request, which reads as "no defects" rather than "not
 * applicable". The API still accepts `paste`; we simply do not offer it until
 * the chart can label a not-applicable source distinctly.
 */
export const DPMO_TREND_OPPORTUNITIES: readonly DpmoOpportunity[] = ["All", "Components"];

/**
 * URL-serialisable filter state for the DPMO-trend-by-line report. Every
 * field lives in the TanStack Router search-params so a full report — window,
 * bucket, opportunity, numerator, skip mode — is shareable / bookmarkable /
 * reloadable verbatim.
 */
export type DpmoTrendSearch = {
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Bucket size. Default `Week`. */
    bucket?: DpmoTrendBucketSize;
    /**
     * Which tested-object kinds count as opportunities. Default `Components`.
     * Changing this refetches: it changes the denominator AND which objects
     * contribute defects.
     */
    opportunity?: DpmoOpportunity;
    /**
     * Displayed defect numerator. Default `Real`. Display-only — the API
     * returns all three on every cell, so switching never refetches.
     */
    numerator?: DpmoNumerator;
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
 * Serialise a {@link DpmoTrendSearch} into the query-string shape the API
 * accepts. NOTE: `numerator` is intentionally omitted — the JSON endpoint
 * returns every numerator, and the toggle is applied client-side. (The PDF
 * export URL adds it separately, because a PDF must commit to one.)
 */
export function toApiQuery(search: DpmoTrendSearch): Record<string, string> {
    const out: Record<string, string> = {};
    if (search.startUtc) out.startUtc = search.startUtc;
    if (search.endUtc) out.endUtc = search.endUtc;
    if (search.bucket) out.bucket = search.bucket;
    if (search.opportunity) out.opportunity = search.opportunity;
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
 * into the typed {@link DpmoTrendSearch} shape, applying the report defaults
 * (Week / Components / Real / Clean).
 */
export function validateDpmoTrendSearch(raw: Record<string, unknown>): DpmoTrendSearch {
    return {
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        bucket: toEnumOrDefault<DpmoTrendBucketSize>(raw.bucket, DPMO_TREND_BUCKETS, "Week"),
        opportunity: toEnumOrDefault<DpmoOpportunity>(
            raw.opportunity,
            DPMO_TREND_OPPORTUNITIES,
            "Components",
        ),
        numerator: toEnumOrDefault<DpmoNumerator>(raw.numerator, DPMO_NUMERATORS, "Real"),
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

/**
 * Coerce the `lines` search value into a number array.
 *
 * MUST handle a NUMBER array, not just strings. `formToSearch` emits
 * `lines` as numbers (e.g. `[2, 7]`) during in-app navigation, and TanStack
 * Router re-runs `validateSearch` on every navigation. An earlier FPY-trend
 * implementation delegated this to a string-only helper, which filtered the
 * numbers straight out — so `lines` silently vanished from both the URL and
 * the API call and no `Machine_Id IN (...)` ever reached SQL. The tell was
 * that string arrays like `skipStatuses` survived while `lines` did not.
 * Covered by dpmo-trend.search.test.ts.
 */
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

function toEnumArray<T extends string>(v: unknown, allowed: readonly T[]): T[] | undefined {
    const allow = allowed as readonly string[];
    let items: string[];
    if (Array.isArray(v)) {
        items = v.filter((x): x is string => typeof x === "string");
    } else if (typeof v === "string") {
        items = v.split(",").map((s) => s.trim()).filter((s) => s.length > 0);
    } else {
        return undefined;
    }
    const out = items.filter((s) => allow.includes(s)) as T[];
    return out.length > 0 ? out : undefined;
}
