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

/** One (machine, product) pair from `/api/sources/{id}/active-filters`. */
export type ActiveFilterPair = {
    machineId: number;
    productId: number;
};

/** `/api/sources/{id}/active-filters` response. */
export type ActiveFilters = {
    pairs: ActiveFilterPair[];
};

/**
 * Distinct (machine, product) pairs that produced a panel inside
 * `[startUtc, endUtc)`. Used to cascade the machine / product filter
 * dropdowns so they only offer combinations that actually ran.
 */
export function fetchActiveFilters(
    sourceId: string,
    startUtc: string,
    endUtc: string,
): Promise<ActiveFilters> {
    const qs = new URLSearchParams({ startUtc, endUtc }).toString();
    return apiFetch<ActiveFilters>(
        `/api/sources/${encodeURIComponent(sourceId)}/active-filters?${qs}`,
    );
}
