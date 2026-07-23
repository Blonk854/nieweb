import { apiFetch } from "./client";

/**
 * Home-page pinned reports (docs/phase-2.md §7.6 `RC4`).
 * Mirrors `Nieweb.Api.Endpoints.ReportEndpoints.HomeReportDto`.
 * Locked pinned reports are included and get a badge on the card.
 */
export type HomeReportDto = {
    id: number;
    title: string;
    description: string | null;
    reportGroupId: number | null;
    groupName: string | null;
    ownerDisplayName: string;
    isLocked: boolean;
    refreshFrequencySeconds: number | null;
    displayOrder: number;
    entityCount: number;
    lastModifiedUtc: string;
};

export function listHomeReports(): Promise<HomeReportDto[]> {
    return apiFetch<HomeReportDto[]>("/api/reports/home");
}
