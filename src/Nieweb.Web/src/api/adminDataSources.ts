import { apiFetch } from "./client";

/**
 * Admin-only AOI data-source management API. Mirrors the .NET
 * endpoints in `Nieweb.Api/Endpoints/AdminDataSourcesEndpoints.cs`
 * (docs/phase-3.md Phase C — Databases settings screen).
 *
 * Every mutating call requires an authenticated Admin role — the API
 * returns 403 otherwise. Passwords are never returned by the server;
 * on update, an empty {@link AoiSourceUpsertRequest.password}
 * preserves the existing encrypted blob so operators can edit
 * metadata without re-typing the credential.
 */

/** Row DTO for a configured AOI source. Never carries a password. */
export type AoiSourceConfigDto = {
    key: string;
    displayName: string;
    kind: string;
    server: string | null;
    database: string | null;
    user: string | null;
    hasPassword: boolean;
    connectTimeoutSeconds: number;
    queryTimeoutSeconds: number;
    trustServerCertificate: boolean;
    encrypt: boolean;
    isEnabled: boolean;
    lastTestedUtc: string | null;
    lastTestSucceeded: boolean | null;
    lastTestError: string | null;
    createdUtc: string;
    lastModifiedUtc: string;
};

/** PUT /api/admin/data-sources/{key} body. */
export type AoiSourceUpsertRequest = {
    displayName: string;
    kind: string;
    server: string | null;
    database: string | null;
    user: string | null;
    /**
     * Leave `null` or empty to preserve the existing encrypted password.
     * Provide a non-empty value to rotate the credential.
     */
    password: string | null;
    connectTimeoutSeconds: number;
    queryTimeoutSeconds: number;
    trustServerCertificate: boolean;
    encrypt: boolean;
    isEnabled: boolean;
};

/** POST /api/admin/data-sources/test body. */
export type AoiSourceTestRequest = AoiSourceUpsertRequest & {
    key: string;
};

/** POST /api/admin/data-sources/test response. */
export type AoiSourceTestResult = {
    ok: boolean;
    durationMs: number;
    errorMessage: string | null;
};

/** GET /api/admin/data-sources/restart-status response. */
export type RestartStatusResponse = {
    pending: boolean;
    setUtc: string | null;
    reason: string | null;
};

export function listAoiSources(): Promise<AoiSourceConfigDto[]> {
    return apiFetch<AoiSourceConfigDto[]>("/api/admin/data-sources");
}

export function getAoiSource(key: string): Promise<AoiSourceConfigDto> {
    return apiFetch<AoiSourceConfigDto>(
        `/api/admin/data-sources/${encodeURIComponent(key)}`,
    );
}

export function upsertAoiSource(
    key: string,
    body: AoiSourceUpsertRequest,
    options?: { ifNoneMatch?: boolean },
): Promise<AoiSourceConfigDto> {
    // `If-None-Match: *` turns the idempotent PUT into a race-safe
    // create — the server (see AdminDataSourcesEndpoints.UpsertAsync)
    // returns 409 Conflict when the row already exists. Only sent in
    // create mode; edit mode omits the header so the same endpoint
    // still behaves as a plain upsert.
    const headers: Record<string, string> = {
        "Content-Type": "application/json",
    };
    if (options?.ifNoneMatch) {
        headers["If-None-Match"] = "*";
    }
    return apiFetch<AoiSourceConfigDto>(
        `/api/admin/data-sources/${encodeURIComponent(key)}`,
        {
            method: "PUT",
            headers,
            body: JSON.stringify(body),
        },
    );
}

export function deleteAoiSource(key: string): Promise<void> {
    return apiFetch<void>(
        `/api/admin/data-sources/${encodeURIComponent(key)}`,
        { method: "DELETE" },
    );
}

export function testAoiSource(
    body: AoiSourceTestRequest,
): Promise<AoiSourceTestResult> {
    return apiFetch<AoiSourceTestResult>(
        "/api/admin/data-sources/test",
        {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        },
    );
}

export function restartApi(): Promise<{ ok: boolean; message: string }> {
    return apiFetch<{ ok: boolean; message: string }>(
        "/api/admin/data-sources/restart",
        { method: "POST" },
    );
}

export function getRestartStatus(): Promise<RestartStatusResponse> {
    return apiFetch<RestartStatusResponse>(
        "/api/admin/data-sources/restart-status",
    );
}

/**
 * Poll `/health/live` (anonymous) until it returns 2xx or the
 * deadline expires. Used after the admin clicks "Restart API" to
 * detect when the new process has accepted connections again.
 */
export async function waitForApi(
    options: {
        timeoutMs?: number;
        intervalMs?: number;
        signal?: AbortSignal;
    } = {},
): Promise<boolean> {
    const timeoutMs = options.timeoutMs ?? 60_000;
    const intervalMs = options.intervalMs ?? 1_000;
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (options.signal?.aborted) {
            return false;
        }
        try {
            const res = await fetch("/health/live", {
                method: "GET",
                cache: "no-store",
                signal: options.signal,
            });
            if (res.ok) {
                return true;
            }
        } catch {
            // Network error — API is still down. Fall through to wait.
        }
        await new Promise((resolve) => setTimeout(resolve, intervalMs));
    }
    return false;
}
