import { apiFetch } from "./client";
import {
    toApiQuery,
    type DpmoNumerator,
    type DpmoOpportunity,
    type DpmoTrendBucketSize,
    type DpmoTrendSearch,
    type SkipExclusion,
} from "../routes/dpmo-trend.search";

/**
 * The shared opportunity denominator plus all three defect numerators for one
 * (line, bucket) cell. Mirrors `Nieweb.Reports.DpmoTrendKpi`.
 *
 * All three DPMO rates ship on every cell so the numerator toggle is
 * display-only — see {@link dpmoFor}.
 */
export type DpmoTrendKpi = {
    opportunityCount: number;
    defectsAoi: number;
    defectsReal: number;
    defectsDummy: number;
    dpmoAoi: number;
    dpmoReal: number;
    dpmoDummy: number;
};

/** One time bucket on the trend X-axis. Mirrors `Nieweb.Reports.DpmoTrendBucket`. */
export type DpmoTrendBucket = {
    index: number;
    label: string;
    startUtc: string;
    endUtcExclusive: string;
};

/** One line's DPMO for one bucket. Mirrors `Nieweb.Reports.DpmoTrendPoint`. */
export type DpmoTrendPoint = {
    bucketIndex: number;
    kpi: DpmoTrendKpi;
};

/** One AOI line's trend series. Mirrors `Nieweb.Reports.DpmoTrendLine`. */
export type DpmoTrendLine = {
    machineId: number;
    machineName: string | null;
    points: DpmoTrendPoint[];
    overall: DpmoTrendKpi;
};

/** One source's trend result. Mirrors `Nieweb.Reports.DpmoTrendResult`. */
export type DpmoTrendSourceResult = {
    source: { id: string; displayName: string };
    bucket: DpmoTrendBucketSize;
    opportunity: DpmoOpportunity;
    skipExclusion: SkipExclusion;
    buckets: DpmoTrendBucket[];
    lines: DpmoTrendLine[];
    skipExcludedCards: number;
};

/** Full response. Mirrors `ReportEndpoints.DpmoTrendReportResponse`. */
export type DpmoTrendReportResponse = {
    bucket: DpmoTrendBucketSize;
    opportunity: DpmoOpportunity;
    skipExclusion: SkipExclusion;
    sources: DpmoTrendSourceResult[];
};

/**
 * Run the DPMO trend report across all sources. Gate behind
 * `Boolean(search.startUtc && search.endUtc)` — the API returns 400 when the
 * window is missing.
 */
export function runDpmoTrendReport(search: DpmoTrendSearch): Promise<DpmoTrendReportResponse> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<DpmoTrendReportResponse>(`/api/reports/dpmo-trend?${qs}`);
}

/**
 * Relative URL for an export endpoint. The PDF variant carries the selected
 * `numerator` (a PDF is a flat artefact and must commit to one; the CSV/XLSX
 * emit all three). Fetch these via the authenticated `downloadWithAuth`
 * helper — a plain `<a href>` carries no bearer token and 401s.
 */
export function dpmoTrendExportUrl(
    search: DpmoTrendSearch,
    format: "csv" | "xlsx" | "pdf",
): string {
    const params = toApiQuery(search);
    if (format === "pdf" && search.numerator) {
        params.numerator = search.numerator;
    }
    const qs = new URLSearchParams(params).toString();
    return `/api/reports/dpmo-trend/export.${format}?${qs}`;
}

/** Read the numerator-selected DPMO rate from a KPI. */
export function dpmoFor(kpi: DpmoTrendKpi, numerator: DpmoNumerator): number {
    if (numerator === "Aoi") return kpi.dpmoAoi;
    if (numerator === "Dummy") return kpi.dpmoDummy;
    return kpi.dpmoReal;
}

/** Read the numerator-selected raw defect count from a KPI. */
export function defectsFor(kpi: DpmoTrendKpi, numerator: DpmoNumerator): number {
    if (numerator === "Aoi") return kpi.defectsAoi;
    if (numerator === "Dummy") return kpi.defectsDummy;
    return kpi.defectsReal;
}
