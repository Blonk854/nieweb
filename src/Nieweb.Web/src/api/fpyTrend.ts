import { apiFetch } from "./client";
import {
    toApiQuery,
    type FpyTrendBucketSize,
    type FpyTrendFlavor,
    type FpyTrendGranularity,
    type FpyTrendSearch,
    type SkipExclusion,
} from "../routes/fpy-trend.search";

/** All three FPY flavours + counts for one scope. Mirrors `Nieweb.Reports.FpyKpi`. */
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

/** One time bucket on the trend X-axis. Mirrors `Nieweb.Reports.FpyTrendBucket`. */
export type FpyTrendBucket = {
    index: number;
    label: string;
    startUtc: string;
    endUtcExclusive: string;
};

/** One line's FPY for one bucket. Mirrors `Nieweb.Reports.FpyTrendPoint`. */
export type FpyTrendPoint = {
    bucketIndex: number;
    kpi: FpyKpi;
};

/** One AOI line's trend series. Mirrors `Nieweb.Reports.FpyTrendLine`. */
export type FpyTrendLine = {
    machineId: number;
    machineName: string | null;
    points: FpyTrendPoint[];
    overall: FpyKpi;
};

/** One source's trend result. Mirrors `Nieweb.Reports.FpyTrendResult`. */
export type FpyTrendSourceResult = {
    source: { id: string; displayName: string };
    bucket: FpyTrendBucketSize;
    granularity: FpyTrendGranularity;
    skipExclusion: SkipExclusion;
    buckets: FpyTrendBucket[];
    lines: FpyTrendLine[];
    skipExcludedRows: number;
};

/** Full response. Mirrors `ReportEndpoints.FpyTrendReportResponse`. */
export type FpyTrendReportResponse = {
    bucket: FpyTrendBucketSize;
    granularity: FpyTrendGranularity;
    skipExclusion: SkipExclusion;
    sources: FpyTrendSourceResult[];
};

/**
 * Run the FPY trend report across all sources. Gate behind
 * `Boolean(search.startUtc && search.endUtc)` — the API returns 400 when the
 * window is missing.
 */
export function runFpyTrendReport(search: FpyTrendSearch): Promise<FpyTrendReportResponse> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<FpyTrendReportResponse>(`/api/reports/fpy-trend?${qs}`);
}

/**
 * Relative URL for an export endpoint. The PDF variant carries the selected
 * `flavor` (the CSV/XLSX always emit all three flavours). Fetch these via the
 * authenticated {@link downloadWithAuth} helper — a plain `<a href>` 401s.
 */
export function fpyTrendExportUrl(
    search: FpyTrendSearch,
    format: "csv" | "xlsx" | "pdf",
): string {
    const params = toApiQuery(search);
    if (format === "pdf" && search.flavor) {
        params.flavor = search.flavor;
    }
    const qs = new URLSearchParams(params).toString();
    return `/api/reports/fpy-trend/export.${format}?${qs}`;
}

/** Read the flavour-selected FPY percent from a KPI. */
export function fpyPercentFor(kpi: FpyKpi, flavor: FpyTrendFlavor): number {
    return flavor === "Aoi" ? kpi.fpyAoiPercent : kpi.fpyDiagnosticPercent;
}
