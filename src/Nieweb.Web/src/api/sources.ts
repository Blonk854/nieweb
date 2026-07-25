import { apiFetch } from "./client";

/**
 * `/api/sources` response item. Matches SourceEndpoints.SourceInfo
 * (System.Text.Json camelCase serialization).
 */
export type SourceInfo = {
    id: string;
    displayName: string;
    schemaVersion: string;
    capabilities: string[];
    latestPanelUtc: string | null;
    available: boolean;
};

/** `/api/sources/{id}/machines` response item. */
export type MachineOption = {
    id: number;
    name: string;
    typeName: string;
};

/**
 * `/api/sources/{id}/operators` response item. Small (a few hundred
 * rows at most on either live DB); the TC2 drill-down caches the full
 * list per source so it can render a name for each
 * `TESTED_OBJECT.Operator_Id` without a per-row round-trip.
 */
export type OperatorOption = {
    id: number;
    name: string;
};

/** `/api/sources/{id}/products` response item. */
export type ProductOption = {
    id: number;
    name: string;
    revision: string | null;
};

export function fetchSources(): Promise<SourceInfo[]> {
    return apiFetch<SourceInfo[]>("/api/sources");
}

export function fetchMachines(sourceId: string): Promise<MachineOption[]> {
    return apiFetch<MachineOption[]>(
        `/api/sources/${encodeURIComponent(sourceId)}/machines`,
    );
}

export function fetchOperators(sourceId: string): Promise<OperatorOption[]> {
    return apiFetch<OperatorOption[]>(
        `/api/sources/${encodeURIComponent(sourceId)}/operators`,
    );
}

export function fetchProducts(sourceId: string): Promise<ProductOption[]> {
    return apiFetch<ProductOption[]>(
        `/api/sources/${encodeURIComponent(sourceId)}/products`,
    );
}
