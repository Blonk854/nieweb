import { apiFetch } from "./client";

/**
 * Admin-only shift-cycle API. Mirrors
 * `Nieweb.Api/Endpoints/AdminShiftsEndpoints.cs` (PL1 of
 * docs/phase-2.md §7.4). The cycle is atomic — GET returns the
 * current breakpoints and PUT replaces the entire list.
 */

export type ShiftBreakpointDto = {
    id: number;
    hour: number;
    minute: number;
    label: string | null;
    displayOrder: number;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type ShiftBreakpointInputDto = {
    hour: number;
    minute: number;
    label?: string | null;
};

export type ReplaceShiftsRequest = {
    entries: ShiftBreakpointInputDto[];
};

export function listShifts(): Promise<ShiftBreakpointDto[]> {
    return apiFetch<ShiftBreakpointDto[]>("/api/admin/shifts");
}

export function replaceShifts(
    body: ReplaceShiftsRequest,
): Promise<ShiftBreakpointDto[]> {
    return apiFetch<ShiftBreakpointDto[]>("/api/admin/shifts", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}
