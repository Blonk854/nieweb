import { apiFetch } from "./client";
import type { SkipSummarySearch } from "../routes/skip-summary.search";
import { toApiQuery } from "../routes/skip-summary.search";

/** The four skip classes. Mirrors `Nieweb.Reports.Common.Skips.SkipClass`. */
export type SkipClass = "None" | "ManualSkip" | "MachineFlagged" | "HeuristicMissing";

/** Card / component tallies for one skip class. Mirrors `SkipClassCount`. */
export type SkipClassCount = {
    class: SkipClass;
    cardCount: number;
    componentCount: number;
    cardPercent: number;
};

export type SkipSummarySourceRef = {
    id: string;
    displayName: string;
};

export type SkipSummaryWindow = {
    startUtc: string;
    endUtcExclusive: string;
};

/** Full skip-summary response. Mirrors `Nieweb.Reports.SkipSummaryResult`. */
export type SkipSummaryResult = {
    source: SkipSummarySourceRef;
    window: SkipSummaryWindow;
    totalCards: number;
    totalComponents: number;
    skippedCards: number;
    skippedCardPercent: number;
    classes: SkipClassCount[];
};

/**
 * Run the skip-summary report for the given filter. Callers should gate
 * this behind `Boolean(search.sourceId && search.startUtc && search.endUtc)`
 * because the API returns 400 when any of those are missing.
 */
export function runSkipSummaryReport(search: SkipSummarySearch): Promise<SkipSummaryResult> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<SkipSummaryResult>(`/api/reports/skip-summary?${qs}`);
}
