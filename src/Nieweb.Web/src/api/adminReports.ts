import { apiFetch } from "./client";

/**
 * Admin-only report-composition API. Mirrors
 * `Nieweb.Api/Endpoints/AdminReportsEndpoints.cs` (backlog RC1).
 *
 * All endpoints are Admin-gated on the server (403 for readers /
 * authors). The RC2 SPA editor is the first consumer; RC3 will add
 * owner-scoped writes once the ownership model lands.
 */

export type ReportGroupDto = {
    id: number;
    name: string;
    displayOrder: number;
    reportCount: number;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type ReportDto = {
    id: number;
    title: string;
    description: string | null;
    reportGroupId: number | null;
    groupName: string | null;
    ownerUserId: number | null;
    ownerDisplayName: string;
    isLocked: boolean;
    isPinnedHome: boolean;
    refreshFrequencySeconds: number | null;
    chromeJson: string | null;
    displayOrder: number;
    entityCount: number;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type ReportEntityDto = {
    id: number;
    reportId: number;
    tileType: string;
    title: string | null;
    displayOrder: number;
    configJson: string;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type ReportDetailDto = {
    report: ReportDto;
    entities: ReportEntityDto[];
};

export type GroupRequest = {
    name: string;
    displayOrder: number;
};

export type CreateReportRequest = {
    title: string;
    description?: string | null;
    reportGroupId?: number | null;
    ownerUserId?: number | null;
    ownerDisplayName: string;
    isLocked: boolean;
    isPinnedHome: boolean;
    refreshFrequencySeconds?: number | null;
    chromeJson?: string | null;
    displayOrder: number;
};

export type UpdateReportRequest = {
    title: string;
    description?: string | null;
    reportGroupId?: number | null;
    isLocked: boolean;
    isPinnedHome: boolean;
    refreshFrequencySeconds?: number | null;
    chromeJson?: string | null;
    displayOrder: number;
};

export type EntityRequest = {
    tileType: string;
    title?: string | null;
    /** `-1` on POST means "append to end". */
    displayOrder: number;
    configJson?: string | null;
};

/** RC3: payload for POST `/{id}/lock` and POST `/{id}/unlock`. */
export type ReportPasswordRequest = {
    password: string;
};

/** RC3: payload for POST `/{id}/duplicate`. Title defaults to
 * `"Copy of {source title}"` when omitted. */
export type DuplicateReportRequest = {
    title?: string | null;
    ownerUserId?: number | null;
    ownerDisplayName: string;
};

// -------------------- Groups --------------------

export function listAdminReportGroups(): Promise<ReportGroupDto[]> {
    return apiFetch<ReportGroupDto[]>("/api/admin/report-groups");
}

export function createAdminReportGroup(body: GroupRequest): Promise<ReportGroupDto> {
    return apiFetch<ReportGroupDto>("/api/admin/report-groups", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateAdminReportGroup(id: number, body: GroupRequest): Promise<ReportGroupDto> {
    return apiFetch<ReportGroupDto>(`/api/admin/report-groups/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function deleteAdminReportGroup(id: number): Promise<void> {
    return apiFetch<void>(`/api/admin/report-groups/${id}`, { method: "DELETE" });
}

// -------------------- Reports --------------------

export function listAdminReports(): Promise<ReportDto[]> {
    return apiFetch<ReportDto[]>("/api/admin/reports");
}

export function getAdminReport(id: number): Promise<ReportDetailDto> {
    return apiFetch<ReportDetailDto>(`/api/admin/reports/${id}`);
}

export function createAdminReport(body: CreateReportRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>("/api/admin/reports", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateAdminReport(id: number, body: UpdateReportRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/admin/reports/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function deleteAdminReport(id: number): Promise<void> {
    return apiFetch<void>(`/api/admin/reports/${id}`, { method: "DELETE" });
}

// -------------------- Tiles (report entities) --------------------

export function addAdminReportEntity(reportId: number, body: EntityRequest): Promise<ReportEntityDto> {
    return apiFetch<ReportEntityDto>(`/api/admin/reports/${reportId}/entities`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateAdminReportEntity(
    reportId: number,
    entityId: number,
    body: EntityRequest,
): Promise<ReportEntityDto> {
    return apiFetch<ReportEntityDto>(`/api/admin/reports/${reportId}/entities/${entityId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function removeAdminReportEntity(reportId: number, entityId: number): Promise<void> {
    return apiFetch<void>(`/api/admin/reports/${reportId}/entities/${entityId}`, {
        method: "DELETE",
    });
}

// -------------------- Lock / unlock / duplicate (RC3) --------------------

export function lockAdminReport(id: number, body: ReportPasswordRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/admin/reports/${id}/lock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function unlockAdminReport(id: number, body: ReportPasswordRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/admin/reports/${id}/unlock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function duplicateAdminReport(id: number, body: DuplicateReportRequest): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/admin/reports/${id}/duplicate`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}


// -------------------- Pin / unpin (F14) --------------------

export function pinAdminReport(id: number): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/admin/reports/${id}/pin`, { method: "POST" });
}

export function unpinAdminReport(id: number): Promise<ReportDto> {
    return apiFetch<ReportDto>(`/api/admin/reports/${id}/unpin`, { method: "POST" });
}