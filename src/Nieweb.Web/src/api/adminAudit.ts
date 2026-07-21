import { apiFetch } from "./client";

/**
 * Admin-only audit trail API. Mirrors
 * `Nieweb.Api/Endpoints/AuditEndpoints.cs`.
 *
 * The endpoint is read-only and returns a paged view over the
 * append-only `AuditEvents` table. All filter parameters are
 * optional; omitting them yields the most recent 100 rows
 * (server-side default), ordered `EventTimeUtc DESC, Id DESC`.
 */

export type AuditEventDto = {
    id: number;
    eventTimeUtc: string;
    actorUserId: number | null;
    actorDisplayName: string;
    eventType: string;
    targetType: string;
    targetId: string;
    detailsJson: string;
    ipAddress: string | null;
};

export type AuditListResponse = {
    items: AuditEventDto[];
    total: number;
    page: number;
    pageSize: number;
};

export type AuditListParams = {
    eventType?: string;
    targetType?: string;
    targetId?: string;
    actorUserId?: number;
    fromUtc?: string;
    toUtc?: string;
    page?: number;
    pageSize?: number;
};

export function listAuditEvents(
    params: AuditListParams = {},
): Promise<AuditListResponse> {
    const query = new URLSearchParams();
    if (params.eventType) query.set("eventType", params.eventType);
    if (params.targetType) query.set("targetType", params.targetType);
    if (params.targetId) query.set("targetId", params.targetId);
    if (params.actorUserId !== undefined) {
        query.set("actorUserId", String(params.actorUserId));
    }
    if (params.fromUtc) query.set("fromUtc", params.fromUtc);
    if (params.toUtc) query.set("toUtc", params.toUtc);
    if (params.page !== undefined) query.set("page", String(params.page));
    if (params.pageSize !== undefined) {
        query.set("pageSize", String(params.pageSize));
    }
    const qs = query.toString();
    const path = qs.length > 0 ? `/api/admin/audit?${qs}` : "/api/admin/audit";
    return apiFetch<AuditListResponse>(path);
}
