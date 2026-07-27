import { apiFetch } from "./client";
import type { FpySearch } from "../routes/fpy.search";
import { toApiQuery } from "../routes/fpy.search";
import type {
    FpyGranularity,
    FpyGroupBy,
    SkipExclusion,
} from "../routes/fpy.search";

/** FPY / status counts for a single scope (row or grand total). Mirrors `Nieweb.Reports.FpyKpi`. */
export type FpyKpi = {
    totalRows: number;
    inspectedCount: number;
    notInspectedCount: number;
    faultyCount: number;
    goodAoiCount: number;
    goodDiagnosticCount: number;
    goodAfterRepairCount: number;
    fpyAoiPercent: number;
    fpyDiagnosticPercent: number;
    fpyAfterRepairPercent: number;
};

/** One row of an FPY table. Mirrors `Nieweb.Reports.FpyTableRow`. */
export type FpyTableRow = {
    groupKey: number;
    groupName: string | null;
    kpi: FpyKpi;
};

export type FpySourceRef = {
    id: string;
    displayName: string;
};

export type FpyWindow = {
    startUtc: string;
    endUtcExclusive: string;
};

/** Full FPY response. Mirrors `Nieweb.Reports.FpyTableResult`. */
export type FpyTableResult = {
    source: FpySourceRef;
    window: FpyWindow;
    granularity: FpyGranularity;
    groupBy: FpyGroupBy;
    overall: FpyKpi;
    rows: FpyTableRow[];
    skipExclusion: SkipExclusion;
    skipExcludedRows: number;
};

/**
 * Run the FPY table report for the given filter. Callers should gate
 * this behind `Boolean(search.sourceId && search.startUtc && search.endUtc)`
 * because the API returns 400 when any of those are missing.
 */
export function runFpyTableReport(search: FpySearch): Promise<FpyTableResult> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<FpyTableResult>(`/api/reports/fpy-table?${qs}`);
}

/**
 * Browser URL for the CSV / XLSX / PDF export endpoints. Feed these to
 * `downloadWithAuth` (CSV/XLSX/PDF) or the PDF preview modal — a plain
 * anchor click can't carry the bearer token and would 401.
 */
export function fpyExportUrl(search: FpySearch, format: "csv" | "xlsx" | "pdf"): string {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return `/api/reports/fpy-table/export.${format}?${qs}`;
}
