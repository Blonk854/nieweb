import { apiFetch } from "./client";
import type { DpmoSearch } from "../routes/dpmo.search";
import { toApiQuery } from "../routes/dpmo.search";
import type {
    DpmoGroupBy,
    DpmoNumerator,
    DpmoOpportunity,
    SkipExclusion,
} from "../routes/dpmo.search";

/** DPMO counts for a single scope (row or grand total). Mirrors `Nieweb.Reports.DpmoKpi`. */
export type DpmoKpi = {
    testedObjectCount: number;
    opportunityCount: number;
    defectBitCount: number;
    dpmoPpm: number;
};

/** One row of a DPMO table. Mirrors `Nieweb.Reports.DpmoTableRow`. */
export type DpmoTableRow = {
    groupKey: string | null;
    groupName: string | null;
    kpi: DpmoKpi;
};

export type DpmoSourceRef = {
    id: string;
    displayName: string;
};

export type DpmoWindow = {
    startUtc: string;
    endUtcExclusive: string;
};

/** Full DPMO response. Mirrors `Nieweb.Reports.DpmoTableResult`. */
export type DpmoTableResult = {
    source: DpmoSourceRef;
    window: DpmoWindow;
    groupBy: DpmoGroupBy;
    numerator: DpmoNumerator;
    opportunity: DpmoOpportunity;
    overall: DpmoKpi;
    rows: DpmoTableRow[];
    skipExclusion: SkipExclusion;
    skipExcludedCards: number;
};

/**
 * Run the DPMO table report for the given filter. Callers should gate
 * this behind
 * `Boolean(search.sourceId && search.startUtc && search.endUtc && search.groupBy)`
 * because the API returns 400 when any of those are missing.
 */
export function runDpmoTableReport(search: DpmoSearch): Promise<DpmoTableResult> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<DpmoTableResult>(`/api/reports/dpmo-table?${qs}`);
}

/**
 * Browser URL for the CSV / XLSX / PDF export endpoints. Rendered as an
 * `<Anchor href>` (CSV / XLSX) or fed to the PDF preview modal. Same
 * auth caveat as the other export URLs: anchor clicks don't forward the
 * Bearer token — a follow-up ticket switches to fetch+blob.
 */
export function dpmoExportUrl(search: DpmoSearch, format: "csv" | "xlsx" | "pdf"): string {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return `/api/reports/dpmo-table/export.${format}?${qs}`;
}
