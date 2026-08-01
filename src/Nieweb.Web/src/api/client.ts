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
    /** Parsed RFC-9457 body, when the server sent `application/problem+json`. */
    public readonly problem?: ProblemDetails;
    /**
     * Stable machine-readable identifier from the problem body's `code`
     * extension (see `ReportEndpoints.ProblemCodes` on the server). Callers
     * map it onto a localized message; `undefined` for legacy / plain-text
     * error responses.
     */
    public readonly code?: string;

    public constructor(status: number, statusText: string, body: string) {
        super(`HTTP ${status} ${statusText}`);
        this.name = "ApiError";
        this.status = status;
        this.statusText = statusText;
        this.body = body;
        this.problem = parseProblemDetails(body);
        this.code = this.problem?.code;
    }
}

/**
 * RFC-9457 problem body as emitted by ASP.NET's `Results.Problem`, plus the
 * Nieweb `code` extension member.
 */
export type ProblemDetails = {
    type?: string;
    title?: string;
    detail?: string;
    status?: number;
    /** Nieweb extension: stable identifier for the failure, e.g. `empty_window`. */
    code?: string;
    /** Present on `ValidationProblem` responses: field name -> messages. */
    errors?: Record<string, string[]>;
};

/**
 * Best-effort parse of an error body. Returns `undefined` for anything that
 * is not a JSON object (plain text, HTML error pages, empty bodies) so
 * callers can fall back to the bare `HTTP <status>` message.
 */
function parseProblemDetails(body: string): ProblemDetails | undefined {
    const trimmed = body.trim();
    if (!trimmed.startsWith("{")) {
        return undefined;
    }
    try {
        const parsed: unknown = JSON.parse(trimmed);
        if (typeof parsed !== "object" || parsed === null) {
            return undefined;
        }
        return parsed as ProblemDetails;
    } catch {
        return undefined;
    }
}

