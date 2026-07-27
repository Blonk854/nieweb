import { createContext, useContext, type ReactNode } from "react";

import {
    addAdminReportEntity,
    duplicateAdminReport,
    getAdminReport,
    listAdminReportGroups,
    lockAdminReport,
    removeAdminReportEntity,
    unlockAdminReport,
    updateAdminReport,
    updateAdminReportEntity,
    type DuplicateReportRequest,
    type EntityRequest,
    type ReportDetailDto,
    type ReportDto,
    type ReportEntityDto,
    type ReportGroupDto,
    type ReportPasswordRequest,
    type UpdateReportRequest,
} from "./adminReports";
import {
    addMyReportEntity,
    duplicateMyReport,
    getMyReport,
    lockMyReport,
    removeMyReportEntity,
    unlockMyReport,
    updateMyReport,
    updateMyReportEntity,
} from "./authorReports";

/**
 * A single reports-composition API used by the shared report editor so
 * the same UI serves both the admin surface (`/api/admin/reports`, all
 * reports) and the self-service author surface (`/api/reports`,
 * own-only). The editor reads the adapter from {@link useReportsApi};
 * each route wraps it in a {@link ReportsApiProvider} with the right
 * adapter. `mode` drives the auth gate, the back link and the query
 * cache keys so the two surfaces never collide.
 */
export type ReportsApiMode = "admin" | "author";

export type ReportsApiAdapter = {
    mode: ReportsApiMode;
    getReport(id: number): Promise<ReportDetailDto>;
    listGroups(): Promise<ReportGroupDto[]>;
    updateReport(id: number, body: UpdateReportRequest): Promise<ReportDto>;
    addEntity(id: number, body: EntityRequest): Promise<ReportEntityDto>;
    updateEntity(id: number, entityId: number, body: EntityRequest): Promise<ReportEntityDto>;
    removeEntity(id: number, entityId: number): Promise<void>;
    lock(id: number, body: ReportPasswordRequest): Promise<ReportDto>;
    unlock(id: number, body: ReportPasswordRequest): Promise<ReportDto>;
    duplicate(id: number, body: DuplicateReportRequest): Promise<ReportDto>;
};

/** Admin adapter — full CRUD across every report. */
export const adminReportsAdapter: ReportsApiAdapter = {
    mode: "admin",
    getReport: getAdminReport,
    listGroups: listAdminReportGroups,
    updateReport: updateAdminReport,
    addEntity: addAdminReportEntity,
    updateEntity: updateAdminReportEntity,
    removeEntity: removeAdminReportEntity,
    lock: lockAdminReport,
    unlock: unlockAdminReport,
    duplicate: duplicateAdminReport,
};

/**
 * Author adapter — own-only writes via `/api/reports`. Groups are an
 * admin-managed concept, so authors get an empty group list (no group
 * assignment). Owner identity and pin state are derived server-side, so
 * the admin-shaped request fields for those are simply dropped here.
 */
export const authorReportsAdapter: ReportsApiAdapter = {
    mode: "author",
    getReport: getMyReport,
    listGroups: () => Promise.resolve([]),
    updateReport: (id, body) =>
        updateMyReport(id, {
            title: body.title,
            description: body.description ?? null,
            reportGroupId: body.reportGroupId ?? null,
            refreshFrequencySeconds: body.refreshFrequencySeconds ?? null,
            chromeJson: body.chromeJson ?? null,
            displayOrder: body.displayOrder,
        }),
    addEntity: addMyReportEntity,
    updateEntity: updateMyReportEntity,
    removeEntity: removeMyReportEntity,
    lock: lockMyReport,
    unlock: unlockMyReport,
    duplicate: (id, body) => duplicateMyReport(id, { title: body.title ?? null }),
};

const ReportsApiContext = createContext<ReportsApiAdapter>(adminReportsAdapter);

export function ReportsApiProvider(props: {
    adapter: ReportsApiAdapter;
    children: ReactNode;
}) {
    return (
        <ReportsApiContext.Provider value={props.adapter}>
            {props.children}
        </ReportsApiContext.Provider>
    );
}

export function useReportsApi(): ReportsApiAdapter {
    return useContext(ReportsApiContext);
}
