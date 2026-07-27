import { apiFetch } from "./client";
import type {
    EntityRequest,
    ReportDetailDto,
    ReportDto,
    ReportEntityDto,
    ReportPasswordRequest,
} from "./adminReports";

/**
 * Self-service report authoring API for `Author` (and `Admin`) users.
 * Mirrors `Nieweb.Api/Endpoints/AuthorReportsEndpoints.cs`, mounted at
 * `/api/reports`. Every call is scoped server-side to the caller's own
 * reports (403 on someone else's; 404 when missing). Owner identity and
 * pin state are never sent from here — the server derives the owner from
 * the auth token and authors cannot pin to the shared home page.
 *
 * Response DTO shapes are identical to the admin surface, so the admin
 * report DTO types are reused verbatim.
 */

export type AuthorCreateReportRequest = {
    title: string;
    description?: string | null;
    reportGroupId?: number | null;
    refreshFrequencySeconds?: number | null;
    chromeJson?: string | null;
    displayOrder: number;
};

export type AuthorUpdateReportRequest = {
    title: string;
    description?: string | null;
    reportGroupId?: number | null;
    refreshFrequencySeconds?: number | null;
    chromeJson?: string | null;
    displayOrder: number;
};

export type AuthorDuplicateReportRequest = {
    title?: string | null;
};

// -------------------- Reports --------------------

export function listMyReports(): Promise<ReportDto[]> {
    return apiFetch<ReportDto[]>("/api/reports/mine");
}

export function getMyReport(id: number): Promise<ReportDetailDto> {
    return apiFetch<ReportDetailDto>(`/api/reports/${id}`);
}

export function createMyReport(body: AuthorCreateReportRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>("/api/reports", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateMyReport(id: number, body: AuthorUpdateReportRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/reports/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function deleteMyReport(id: number): Promise<void> {
    return apiFetch<void>(`/api/reports/${id}`, { method: "DELETE" });
}

// -------------------- Tiles --------------------

export function addMyReportEntity(reportId: number, body: EntityRequest): Promise<ReportEntityDto> {
    return apiFetch<ReportEntityDto>(`/api/reports/${reportId}/entities`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateMyReportEntity(
    reportId: number,
    entityId: number,
    body: EntityRequest,
): Promise<ReportEntityDto> {
    return apiFetch<ReportEntityDto>(`/api/reports/${reportId}/entities/${entityId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function removeMyReportEntity(reportId: number, entityId: number): Promise<void> {
    return apiFetch<void>(`/api/reports/${reportId}/entities/${entityId}`, {
        method: "DELETE",
    });
}

// -------------------- Lock / unlock / duplicate --------------------

export function lockMyReport(id: number, body: ReportPasswordRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/reports/${id}/lock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function unlockMyReport(id: number, body: ReportPasswordRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/reports/${id}/unlock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function duplicateMyReport(
    id: number,
    body: AuthorDuplicateReportRequest,
): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/reports/${id}/duplicate`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}
