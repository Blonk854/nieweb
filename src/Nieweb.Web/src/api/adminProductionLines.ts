import { apiFetch } from "./client";

/**
 * Admin-only production-line API. Mirrors
 * `Nieweb.Api/Endpoints/AdminProductionLinesEndpoints.cs` (PL1 of
 * docs/phase-2.md §7.4). Every write is audited server-side.
 *
 * Uniqueness: line names are unique; a physical machine
 * (`sourceId` + `machineId`) belongs to at most one line at a time.
 * Both violations surface as HTTP 409 with a plain-text body.
 */

export type ProductionLineDto = {
    id: number;
    name: string;
    displayOrder: number;
    machineCount: number;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type ProductionLineMachineDto = {
    id: number;
    productionLineId: number;
    sourceId: string;
    machineId: number;
    machineName: string;
    category: string | null;
    displayOrder: number;
    createdUtc: string;
};

export type ProductionLineDetailDto = {
    line: ProductionLineDto;
    machines: ProductionLineMachineDto[];
};

export type CreateLineRequest = {
    name: string;
    displayOrder: number;
};

export type UpdateLineRequest = CreateLineRequest;

export type AddMachineRequest = {
    sourceId: string;
    machineId: number;
    machineName: string;
    category?: string | null;
    displayOrder: number;
};

export function listProductionLines(): Promise<ProductionLineDto[]> {
    return apiFetch<ProductionLineDto[]>("/api/admin/production-lines");
}

export function getProductionLine(
    id: number,
): Promise<ProductionLineDetailDto> {
    return apiFetch<ProductionLineDetailDto>(
        `/api/admin/production-lines/${id}`,
    );
}

export function createProductionLine(
    body: CreateLineRequest,
): Promise<ProductionLineDto> {
    return apiFetch<ProductionLineDto>("/api/admin/production-lines", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateProductionLine(
    id: number,
    body: UpdateLineRequest,
): Promise<ProductionLineDto> {
    return apiFetch<ProductionLineDto>(
        `/api/admin/production-lines/${id}`,
        {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        },
    );
}

export function deleteProductionLine(id: number): Promise<void> {
    return apiFetch<void>(`/api/admin/production-lines/${id}`, {
        method: "DELETE",
    });
}

export function addProductionLineMachine(
    id: number,
    body: AddMachineRequest,
): Promise<ProductionLineMachineDto> {
    return apiFetch<ProductionLineMachineDto>(
        `/api/admin/production-lines/${id}/machines`,
        {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        },
    );
}

export function removeProductionLineMachine(
    lineId: number,
    machineAssignmentId: number,
): Promise<void> {
    return apiFetch<void>(
        `/api/admin/production-lines/${lineId}/machines/${machineAssignmentId}`,
        { method: "DELETE" },
    );
}
