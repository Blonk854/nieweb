import type { SourceInfo } from "../api/sources";

/**
 * FPY aggregation granularity. `Panel` uses `PANELS.Panel_Status`;
 * `Board` uses `CARDS.Card_Status`. Matches the .NET `FpyGranularity`.
 */
export type FpyGranularity = "Panel" | "Board";

export const FPY_GRANULARITIES: readonly FpyGranularity[] = ["Panel", "Board"];

/** FPY column-grouping axis. Matches the .NET `FpyGroupBy`. */
export type FpyGroupBy = "AoiMachine" | "Product";

export const FPY_GROUP_BYS: readonly FpyGroupBy[] = ["AoiMachine", "Product"];

/**
 * Whether the KPI counts the raw inspected population (`Raw`, legacy
 * Vieweb parity) or first excludes skipped / empty boards (`Clean`).
 * Mirrors the .NET `SkipExclusion` enum.
 */
export type SkipExclusion = "Raw" | "Clean";

export const SKIP_EXCLUSIONS: readonly SkipExclusion[] = ["Raw", "Clean"];

/**
 * The four skip classes. Mirrors the .NET
 * <c>Nieweb.Reports.Common.Skips.SkipClass</c> enum. Used as a positive
 * narrowing filter (keep only boards whose class is in the set).
 */
export type SkipStatus = "None" | "ManualSkip" | "MachineFlagged" | "HeuristicMissing";

export const SKIP_STATUS_VALUES: readonly SkipStatus[] = [
    "None",
    "ManualSkip",
    "MachineFlagged",
    "HeuristicMissing",
];

/**
 * URL-serialisable filter state for the FPY report. Every field lives
 * in the search params (see router.ts::fpyRoute.validateSearch) so a
 * full report is shareable, bookmarkable, and reloadable verbatim.
 */
export type FpySearch = {
    /** SourceDescriptor.Id, case-insensitive. */
    sourceId?: string;
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Panel-level or board-level FPY. Default `Panel`. */
    granularity?: FpyGranularity;
    /** Row grouping axis. Default `AoiMachine`. */
    groupBy?: FpyGroupBy;
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
    /**
     * Skip handling. The server default is `Raw`, so the URL only
     * carries this when the user switches to `Clean`.
     */
    skipExclusion?: SkipExclusion;
    /**
     * Positive narrowing filter on the computed per-board skip class:
     * when set, only boards whose class is in the list contribute.
     * Composes with `skipExclusion`. Empty / absent applies no narrowing.
     */
    skipStatuses?: SkipStatus[];
    /** Drop products whose name contains "NOGO" (case-insensitive). */
    excludeNogo?: boolean;
};

/**
 * Serialise an {@link FpySearch} into the query-string shape the API
 * accepts (comma-separated id lists, canonical enum names, no keys
 * when empty / default).
 */
export function toApiQuery(search: FpySearch): Record<string, string> {
    const out: Record<string, string> = {};
    if (search.sourceId) out.sourceId = search.sourceId;
    if (search.startUtc) out.startUtc = search.startUtc;
    if (search.endUtc) out.endUtc = search.endUtc;
    if (search.granularity) out.granularity = search.granularity;
    if (search.groupBy) out.groupBy = search.groupBy;
    if (search.machineIds && search.machineIds.length > 0) {
        out.machineIds = search.machineIds.join(",");
    }
    if (search.productIds && search.productIds.length > 0) {
        out.productIds = search.productIds.join(",");
    }
    // Server default is true; only carry the flag when explicitly off.
    if (search.onlyLastInspection === false) {
        out.onlyLastInspection = "false";
    }
    // Server default is Raw; only carry the flag when Clean.
    if (search.skipExclusion === "Clean") {
        out.skipExclusion = "Clean";
    }
    if (search.skipStatuses && search.skipStatuses.length > 0) {
        out.skipStatuses = search.skipStatuses.join(",");
    }
    if (search.excludeNogo) {
        out.excludeNogo = "true";
    }
    return out;
}

/**
 * Validator for TanStack Router's `validateSearch`. Coerces raw URL
 * values into the typed {@link FpySearch} shape. Unknown keys and
 * malformed enum values are dropped.
 */
export function validateFpySearch(raw: Record<string, unknown>): FpySearch {
    return {
        sourceId: toStringOrUndef(raw.sourceId),
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        granularity: toEnumOrUndef<FpyGranularity>(raw.granularity, FPY_GRANULARITIES),
        groupBy: toEnumOrUndef<FpyGroupBy>(raw.groupBy, FPY_GROUP_BYS),
        machineIds: toNumberArray(raw.machineIds),
        productIds: toNumberArray(raw.productIds),
        onlyLastInspection: toBoolOrUndef(raw.onlyLastInspection),
        skipExclusion: toEnumOrUndef<SkipExclusion>(raw.skipExclusion, SKIP_EXCLUSIONS),
        skipStatuses: toEnumArray<SkipStatus>(raw.skipStatuses, SKIP_STATUS_VALUES),
        excludeNogo: toBoolOrUndef(raw.excludeNogo),
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

function toEnumOrUndef<T extends string>(
    v: unknown,
    allowed: readonly T[],
): T | undefined {
    if (typeof v !== "string") return undefined;
    return (allowed as readonly string[]).includes(v) ? (v as T) : undefined;
}

function toEnumArray<T extends string>(
    v: unknown,
    allowed: readonly T[],
): T[] | undefined {
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
