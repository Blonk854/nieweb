import type { SourceInfo } from "../api/sources";
import {
    SKIP_EXCLUSIONS,
    SKIP_STATUS_VALUES,
    type SkipExclusion,
    type SkipStatus,
} from "./dpmo.search";

// Re-export the skip enums so the Pareto route imports them from one
// place (they are defined once in dpmo.search and shared with FPY/DPMO).
export { SKIP_EXCLUSIONS, SKIP_STATUS_VALUES };
export type { SkipExclusion, SkipStatus };

/**
 * Category axis a Pareto chart groups on. String literals match the
 * .NET `ParetoAxis` enum name-for-name — the API accepts both the
 * string form (`axis=Product`) and dash-lowered aliases
 * (`axis=aoi-machine`), but the SPA always emits the canonical
 * name via {@link toApiQuery}.
 */
export type ParetoAxis =
    | "Defect"
    | "Product"
    | "AoiMachine"
    | "ReferenceDesignator"
    | "PartNumber"
    | "Jedec"
    | "Day"
    | "Shift";

export const PARETO_AXES: readonly ParetoAxis[] = [
    "Defect",
    "Product",
    "AoiMachine",
    "ReferenceDesignator",
    "PartNumber",
    "Jedec",
    "Day",
    "Shift",
];

/** Post-review is the boss-approved default (matches Vieweb "DPMO real defects"). */
export type ParetoNumerator = "Aoi" | "Real" | "Dummy";

export const PARETO_NUMERATORS: readonly ParetoNumerator[] = ["Aoi", "Real", "Dummy"];

/** Denominator scope. Matches DpmoOpportunity on the server. */
export type ParetoOpportunity = "All" | "Components" | "Paste";

export const PARETO_OPPORTUNITIES: readonly ParetoOpportunity[] = [
    "All",
    "Components",
    "Paste",
];

/**
 * Bar-height metric. `Count` is the volume-weighted default;
 * `Dpmo` / `Ppm` switch to a rate view (see CR1 in docs/phase-2.md).
 * `Ppm` is a display alias for `Dpmo` — the API returns identical
 * numeric values.
 */
export type ParetoWeight = "Count" | "Dpmo" | "Ppm";

export const PARETO_WEIGHTS: readonly ParetoWeight[] = ["Count", "Dpmo", "Ppm"];

/**
 * URL-serialisable filter state for the Pareto report. Every field is
 * URL-encoded via TanStack Router's search-params (see
 * router.ts::paretoRoute.validateSearch) so a full report — source,
 * window, axis, drill-in filters — can be shared, bookmarked and
 * reloaded verbatim.
 */
export type ParetoSearch = {
    /** SourceDescriptor.Id, case-insensitive. */
    sourceId?: string;
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Primary category axis. Default `Defect`. */
    axis?: ParetoAxis;
    /** Which defect bits count. Default `Real`. */
    numerator?: ParetoNumerator;
    /** Which tested-object kinds count as opportunities. Default `All`. */
    opportunity?: ParetoOpportunity;
    /** Bar-height metric. Default `Count`. */
    weight?: ParetoWeight;
    /**
     * IANA or Windows time-zone id used to bucket panel timestamps
     * when axis is `Day` or `Shift`. Default UTC.
     */
    siteTimeZone?: string;
    /**
     * Shift start times as an ordered list of `HH:MM` strings.
     * Required when axis is `Shift`.
     */
    shifts?: string[];
    /** Cap on the number of visible bars; excess rolls into an Others bucket. */
    topN?: number;
    /** Cumulative-% threshold that highlights the "vital few". Default 80. */
    vitalFewThreshold?: number;
    /** Panel machine ids. */
    machineIds?: number[];
    /** Panel product ids. */
    productIds?: number[];
    /** 1-based defect bit numbers used for drill-in from the Defect axis. */
    defectBits?: number[];
    /** Reference-designator narrowing filter. */
    topologies?: string[];
    /** Part-number narrowing filter. */
    partNumbers?: string[];
    /** JEDEC / package narrowing filter. */
    jedecNames?: string[];
    /** Skip-exclusion mode: `Raw` (default) or `Clean` (drop skipped boards). */
    skipExclusion?: SkipExclusion;
    /** Narrow to specific skip classes (e.g. only ManualSkip). */
    skipStatuses?: SkipStatus[];
    /** Drop products whose name contains "NOGO" (case-insensitive). */
    excludeNogo?: boolean;
};

/**
 * Serialise a {@link ParetoSearch} into the CSV-formatted query-string
 * shape the API endpoints accept (comma-separated id lists, canonical
 * enum names, no keys when empty).
 */
export function toApiQuery(search: ParetoSearch): Record<string, string> {
    const out: Record<string, string> = {};
    if (search.sourceId) out.sourceId = search.sourceId;
    if (search.startUtc) out.startUtc = search.startUtc;
    if (search.endUtc) out.endUtc = search.endUtc;
    if (search.axis) out.axis = search.axis;
    if (search.numerator) out.numerator = search.numerator;
    if (search.opportunity) out.opportunity = search.opportunity;
    if (search.weight) out.weight = search.weight;
    if (search.siteTimeZone) out.siteTimeZone = search.siteTimeZone;
    if (search.shifts && search.shifts.length > 0) {
        out.shifts = search.shifts.join(",");
    }
    if (typeof search.topN === "number" && search.topN > 0) {
        out.topN = String(search.topN);
    }
    if (typeof search.vitalFewThreshold === "number") {
        out.vitalFewThreshold = String(search.vitalFewThreshold);
    }
    if (search.machineIds && search.machineIds.length > 0) {
        out.machineIds = search.machineIds.join(",");
    }
    if (search.productIds && search.productIds.length > 0) {
        out.productIds = search.productIds.join(",");
    }
    if (search.defectBits && search.defectBits.length > 0) {
        out.defectBits = search.defectBits.join(",");
    }
    if (search.topologies && search.topologies.length > 0) {
        out.topologies = search.topologies.join(",");
    }
    if (search.partNumbers && search.partNumbers.length > 0) {
        out.partNumbers = search.partNumbers.join(",");
    }
    if (search.jedecNames && search.jedecNames.length > 0) {
        out.jedecNames = search.jedecNames.join(",");
    }
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
 * values into the typed {@link ParetoSearch} shape. Unknown keys and
 * malformed enum values are dropped.
 */
export function validateParetoSearch(raw: Record<string, unknown>): ParetoSearch {
    return {
        sourceId: toStringOrUndef(raw.sourceId),
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        axis: toEnumOrUndef<ParetoAxis>(raw.axis, PARETO_AXES),
        numerator: toEnumOrUndef<ParetoNumerator>(raw.numerator, PARETO_NUMERATORS),
        opportunity: toEnumOrUndef<ParetoOpportunity>(
            raw.opportunity,
            PARETO_OPPORTUNITIES,
        ),
        weight: toEnumOrUndef<ParetoWeight>(raw.weight, PARETO_WEIGHTS),
        siteTimeZone: toStringOrUndef(raw.siteTimeZone),
        shifts: toStringArray(raw.shifts),
        topN: toPositiveIntOrUndef(raw.topN),
        vitalFewThreshold: toFiniteNumberOrUndef(raw.vitalFewThreshold),
        machineIds: toNumberArray(raw.machineIds),
        productIds: toNumberArray(raw.productIds),
        defectBits: toNumberArray(raw.defectBits),
        topologies: toStringArray(raw.topologies),
        partNumbers: toStringArray(raw.partNumbers),
        jedecNames: toStringArray(raw.jedecNames),
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

/**
 * Add `bit` to `search.defectBits` (deduplicating, preserving order).
 * Used by the chart's click handler when drilling from a Defect-axis
 * bar into narrower axes.
 */
export function withDefectBit(search: ParetoSearch, bit: number): ParetoSearch {
    if (!Number.isFinite(bit) || !Number.isInteger(bit) || bit <= 0) {
        return search;
    }
    const existing = search.defectBits ?? [];
    if (existing.includes(bit)) return search;
    return { ...search, defectBits: [...existing, bit] };
}

/**
 * Remove `bit` from `search.defectBits`. Returns the same search when
 * the bit was not present. Used by breadcrumb chips.
 */
export function withoutDefectBit(search: ParetoSearch, bit: number): ParetoSearch {
    const existing = search.defectBits ?? [];
    if (!existing.includes(bit)) return search;
    const next = existing.filter((b) => b !== bit);
    return { ...search, defectBits: next.length > 0 ? next : undefined };
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

function toEnumOrUndef<T extends string>(
    v: unknown,
    allowed: readonly T[],
): T | undefined {
    if (typeof v !== "string") return undefined;
    // Case-insensitive comparison against the canonical names so
    // callers can freely mix "product" / "Product" in URLs.
    const normalised = v.trim();
    if (normalised.length === 0) return undefined;
    const match = allowed.find((a) => a.toLowerCase() === normalised.toLowerCase());
    return match;
}

function toEnumArray<T extends string>(
    v: unknown,
    allowed: readonly T[],
): T[] | undefined {
    const strs = toStringArray(v);
    if (!strs) return undefined;
    const matched = strs
        .map((s) => allowed.find((a) => a.toLowerCase() === s.toLowerCase()))
        .filter((x): x is T => x !== undefined);
    return matched.length > 0 ? matched : undefined;
}

function toPositiveIntOrUndef(v: unknown): number | undefined {
    if (typeof v === "number" && Number.isInteger(v) && v > 0) return v;
    if (typeof v === "string") {
        const n = Number(v);
        if (Number.isFinite(n) && Number.isInteger(n) && n > 0) return n;
    }
    return undefined;
}

function toFiniteNumberOrUndef(v: unknown): number | undefined {
    if (typeof v === "number" && Number.isFinite(v)) return v;
    if (typeof v === "string" && v.trim().length > 0) {
        const n = Number(v);
        if (Number.isFinite(n)) return n;
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

function toStringArray(v: unknown): string[] | undefined {
    if (Array.isArray(v)) {
        const strs = v
            .filter((x) => typeof x === "string")
            .map((x) => (x as string).trim())
            .filter((x) => x.length > 0);
        return strs.length > 0 ? strs : undefined;
    }
    if (typeof v === "string") {
        const strs = v
            .split(",")
            .map((s) => s.trim())
            .filter((s) => s.length > 0);
        return strs.length > 0 ? strs : undefined;
    }
    return undefined;
}
