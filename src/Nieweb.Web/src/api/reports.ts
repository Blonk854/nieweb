import { apiFetch } from "./client";
import type { PanelYieldSearch } from "../routes/panel-yield.search";
import { toApiQuery } from "../routes/panel-yield.search";

/** Overall + per-machine KPI shape returned by `/api/reports/panel-yield`. */
export type PanelYieldKpi = {
    totalPanels: number;
    inspectedPanels: number;
    goodPanels: number;
    faultyPanels: number;
    notInspectedPanels: number;
    fpyPercent: number;
};

export type PanelYieldByMachineRow = {
    machineId: number;
    machineName: string | null;
    kpi: PanelYieldKpi;
};

export type PanelYieldSourceRef = {
    id: string;
    displayName: string;
};

export type PanelYieldWindow = {
    startUtc: string;
    endUtcExclusive: string;
};

export type PanelYieldResult = {
    source: PanelYieldSourceRef;
    window: PanelYieldWindow;
    overall: PanelYieldKpi;
    byMachine: PanelYieldByMachineRow[];
};

/**
 * Run the panel-yield report for the given filter. Callers should
 * gate this behind `Boolean(search.sourceId && search.startUtc && search.endUtc)`
 * because the API returns 400 when any of those are missing.
 */
export function runPanelYieldReport(search: PanelYieldSearch): Promise<PanelYieldResult> {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return apiFetch<PanelYieldResult>(`/api/reports/panel-yield?${qs}`);
}

/**
 * Build the browser URL for the CSV/XLSX export endpoints so the user
 * can hit them via `<Anchor href>` without going through fetch (browsers
 * handle the file download natively). The Authorization header is not
 * forwarded on plain <a> clicks - later, F5+ or the export item will
 * either switch to a fetch+blob+object-URL pattern or use signed URLs.
 */
export function panelYieldExportUrl(
    search: PanelYieldSearch,
    format: "csv" | "xlsx" | "pdf",
): string {
    const qs = new URLSearchParams(toApiQuery(search)).toString();
    return `/api/reports/panel-yield/export.${format}?${qs}`;
}
