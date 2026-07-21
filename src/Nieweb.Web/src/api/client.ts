import { useSessionStore } from "../state/session";

/**
 * Minimal fetch wrapper: injects the current bearer token from the
 * session store, throws on non-2xx so TanStack Query routes them into
 * the `error` state, and clears the session on 401 for any endpoint
 * other than /auth/login (so expired tokens don't leave a stale
 * "signed in" indicator lingering in the header).
 *
 * Kept intentionally tiny for F2 - a real client (openapi-fetch or
 * hand-rolled per-endpoint hooks) lands with F4/F5.
 */
export async function apiFetch<T>(
    path: string,
    init?: RequestInit,
): Promise<T> {
    const token = useSessionStore.getState().token;
    const headers = new Headers(init?.headers);
    if (token && !headers.has("Authorization")) {
        headers.set("Authorization", `Bearer ${token}`);
    }
    const response = await fetch(path, { ...init, headers });
    if (!response.ok) {
        const body = await response.text().catch(() => "");
        // Auto-clear the session on token expiry / revocation so the
        // UI doesn't keep pretending we're signed in. /auth/login is
        // excluded because a 401 there just means "wrong password" -
        // the store has no valid session in the first place.
        if (response.status === 401 && !path.startsWith("/auth/login")) {
            useSessionStore.getState().clear();
        }
        throw new ApiError(response.status, response.statusText, body);
    }
    // Some endpoints (204 etc.) return no body.
    if (response.status === 204) {
        return undefined as T;
    }
    return response.json() as Promise<T>;
}

export class ApiError extends Error {
    public readonly status: number;
    public readonly statusText: string;
    public readonly body: string;

    public constructor(status: number, statusText: string, body: string) {
        super(`HTTP ${status} ${statusText}`);
        this.name = "ApiError";
        this.status = status;
        this.statusText = statusText;
        this.body = body;
    }
}
