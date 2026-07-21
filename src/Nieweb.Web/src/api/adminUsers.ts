import { apiFetch } from "./client";

/**
 * Admin-only user management API. Mirrors
 * `Nieweb.Api/Endpoints/AdminUsersEndpoints.cs`.
 *
 * All calls require an authenticated Admin role — the API returns 403
 * otherwise and the client should route the user back to a "forbidden"
 * screen (see routes/admin-users.tsx).
 */

export type AdminUserDto = {
    id: number;
    email: string;
    displayName: string;
    isDisabled: boolean;
    isOidcProvisioned: boolean;
    roles: string[];
    createdUtc: string;
    lastLoginUtc: string | null;
};

export type CreateUserRequest = {
    email: string;
    displayName: string;
    password: string;
    roles: string[];
};

export type UpdateUserRequest = {
    displayName: string;
    isDisabled: boolean;
    roles: string[];
};

export type ResetPasswordRequest = {
    newPassword: string;
};

export function listAdminUsers(): Promise<AdminUserDto[]> {
    return apiFetch<AdminUserDto[]>("/api/admin/users");
}

export function createAdminUser(body: CreateUserRequest): Promise<AdminUserDto> {
    return apiFetch<AdminUserDto>("/api/admin/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateAdminUser(
    id: number,
    body: UpdateUserRequest,
): Promise<AdminUserDto> {
    return apiFetch<AdminUserDto>(`/api/admin/users/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function resetAdminUserPassword(
    id: number,
    body: ResetPasswordRequest,
): Promise<void> {
    return apiFetch<void>(`/api/admin/users/${id}/reset-password`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}
