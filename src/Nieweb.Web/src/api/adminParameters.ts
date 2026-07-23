import { apiFetch } from "./client";

/**
 * Admin-only application-parameter API. Mirrors
 * `Nieweb.Api/Endpoints/AdminParametersEndpoints.cs` (RI3 of
 * docs/phase-2.md §7.1) — the internal `AppParameter` table backs
 * every tolerance interval, MSA constant, and site knob.
 *
 * System rows (`isSystem: true`) can be updated but not deleted — the
 * DELETE endpoint returns HTTP 409 with a plain-text body.
 */

export type AppParameterValueType = "decimal" | "int" | "bool" | "string";

export const APP_PARAMETER_VALUE_TYPES: readonly AppParameterValueType[] = [
    "decimal",
    "int",
    "bool",
    "string",
] as const;

export type AdminParameterDto = {
    key: string;
    valueType: AppParameterValueType;
    value: string;
    description: string | null;
    isSystem: boolean;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type UpsertParameterRequest = {
    valueType: AppParameterValueType;
    value: string;
    description?: string | null;
};

export function listAdminParameters(): Promise<AdminParameterDto[]> {
    return apiFetch<AdminParameterDto[]>("/api/admin/parameters");
}

export function upsertAdminParameter(
    key: string,
    body: UpsertParameterRequest,
): Promise<AdminParameterDto> {
    return apiFetch<AdminParameterDto>(
        `/api/admin/parameters/${encodeURIComponent(key)}`,
        {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        },
    );
}

export function deleteAdminParameter(key: string): Promise<void> {
    return apiFetch<void>(
        `/api/admin/parameters/${encodeURIComponent(key)}`,
        { method: "DELETE" },
    );
}
