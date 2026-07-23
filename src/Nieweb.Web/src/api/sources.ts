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

export function fetchProducts(sourceId: string): Promise<ProductOption[]> {
    return apiFetch<ProductOption[]>(
        `/api/sources/${encodeURIComponent(sourceId)}/products`,
    );
}
