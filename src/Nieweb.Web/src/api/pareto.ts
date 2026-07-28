import { apiFetch } from "./client";
import type { ParetoSearch } from "../routes/pareto.search";
import { toApiQuery } from "../routes/pareto.search";
import type {
    ParetoAxis,
    ParetoNumerator,
    ParetoOpportunity,
} from "../routes/pareto.search";

/** One bar of a Pareto chart. Mirrors `Nieweb.Reports.ParetoRow`. */
export type ParetoRow = {
    groupKey: string | null;
    groupName: string | null;
    defectCount: number;
    weightedScore: number;
    opportunityCount: number;
    opportunitySharePercent: number;
    dpmoPpm: number;
    defectSharePercent: number;
    cumulativePercent: number;
    isVitalFew: boolean;
};

/** DPMO-flavoured overall KPI. Mirrors `Nieweb.Reports.DpmoKpi`. */
export type DpmoKpi = {
    testedObjectCount: number;
    opportunityCount: number;
    defectBitCount: number;
    dpmoPpm: number;
};

/** Echo of every narrowing filter honoured by a specific Pareto run. */
export type ParetoAppliedFilters = {
    machineIds: number[];
    productIds: number[];
    defectBits: number[];
    topologies: string[];
    partNumbers: string[];
    jedecNames: string[];
};

export type ParetoSourceRef = {
    id: string;
    displayName: string;
};

export type ParetoWindow = {
    startUtc: string;
    endUtcExclusive: string;
};

/** Full Pareto response. Mirrors `Nieweb.Reports.ParetoResult`. */
export type ParetoResult = {
    source: ParetoSourceRef;
    window: ParetoWindow;
    axis: ParetoAxis;
    numerator: ParetoNumerator;
    opportunity: ParetoOpportunity;
    weight: "Count";
    appliedFilters: ParetoAppliedFilters;
    overall: DpmoKpi;
    rows: ParetoRow[];
    othersBucket: ParetoRow | null;
    skipExclusion: "Raw" | "Clean";
    skipExcludedCards: number;
};

/**
 * Run the Pareto report for the given filter. Callers should gate
 * this behind `Boolean(search.sourceId && search.startUtc && search.endUtc && search.axis)`
 * because the API returns 400 when any of those are missing.
 */
export function runParetoReport(search: ParetoSearch): Promise<ParetoResult> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<ParetoResult>(`/api/reports/pareto?${qs}`);
}

/**
 * Browser URL for the CSV / XLSX export endpoints. Rendered as an
 * `<Anchor href>` so the browser handles the download natively. Note:
 * the same auth caveat as `panelYieldExportUrl` applies — anchor
 * clicks don't forward the Bearer token; a follow-up ticket will
 * switch to fetch+blob+object-URL or signed URLs.
 */
export function paretoExportUrl(search: ParetoSearch, format: "csv" | "xlsx" | "pdf"): string {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return `/api/reports/pareto/export.${format}?${qs}`;
}
