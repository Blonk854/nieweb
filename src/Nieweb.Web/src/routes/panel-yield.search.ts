import type { SourceInfo } from "../api/sources";

/**
 * Filter state for the Panel Yield by Line report. Every field is
 * URL-serialised via TanStack Router's search-params (see router.ts's
 * validateSearch on the panel-yield route) so the whole report state -
 * source, window, machine/product/recipe selection, last-inspection
 * toggle - can be shared, bookmarked, and reloaded verbatim.
 */
export type PanelYieldSearch = {
    /** SourceDescriptor.Id, case-insensitive. */
    sourceId?: string;
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Integer machine ids. */
    machineIds?: number[];
    /** Integer product ids. */
    productIds?: number[];
    /** Integer recipe ids. */
    recipeIds?: number[];
    /** Post-reflow sources only; ignored otherwise. */
    onlyLastInspection?: boolean;
};

/**
 * Serialize a `PanelYieldSearch` into the CSV-formatted query-string
 * shape the API endpoints accept (comma-separated id lists, ISO-8601
 * timestamps, no id keys when empty).
 */
export function toApiQuery(search: PanelYieldSearch): Record<string, string> {
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
    if (search.recipeIds && search.recipeIds.length > 0) {
        out.recipeIds = search.recipeIds.join(",");
    }
    if (typeof search.onlyLastInspection === "boolean") {
        out.onlyLastInspection = String(search.onlyLastInspection);
    }
    return out;
}

/**
 * Validator for TanStack Router's `validateSearch`. Coerces raw URL
 * values (which may be strings, arrays of strings, or missing) into
 * the typed `PanelYieldSearch` shape. Unknown keys are dropped.
 */
export function validatePanelYieldSearch(raw: Record<string, unknown>): PanelYieldSearch {
    return {
        sourceId: toStringOrUndef(raw.sourceId),
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        machineIds: toNumberArray(raw.machineIds),
        productIds: toNumberArray(raw.productIds),
        recipeIds: toNumberArray(raw.recipeIds),
        onlyLastInspection: toBoolOrUndef(raw.onlyLastInspection),
    };
}

function toStringOrUndef(v: unknown): string | undefined {
    if (typeof v !== "string") return undefined;
    const trimmed = v.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}

function toBoolOrUndef(v: unknown): boolean | undefined {
    if (typeof v === "boolean") return v;
    if (v === "true") return true;
    if (v === "false") return false;
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

/**
 * Given the source-list response, pick a sensible default source id
 * when the URL does not specify one. Prefers the first available
 * source; falls back to the first source overall.
 */
export function pickDefaultSourceId(sources: readonly SourceInfo[]): string | undefined {
    if (sources.length === 0) return undefined;
    return (sources.find((s) => s.available) ?? sources[0]).id;
}
