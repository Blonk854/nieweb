import { ApiError } from "./client";
import { useSessionStore } from "../state/session";

/**
 * Read-side client for the board-SVG cache
 * (docs/phase-2.md §7.5 TC4 Phase C):
 * `GET /api/board-svgs/{productName}` returns the raw cached SVG for
 * a product. Unlike {@link import("./client").apiFetch}, this helper
 * returns the response body as text (not JSON) and threads the ETag
 * back to the caller so React Query can dedupe on it.
 *
 * Behaviour mirrors the server contract:
 *  - 200 → returns SVG source text.
 *  - 304 → treated as "unchanged"; caller keeps the cached copy.
 *  - 400 (bad name) / 404 (not yet cached) / 5xx → throws {@link ApiError}
 *    so TanStack Query surfaces them in `error`.
 */
export type BoardSvgFetchResult = {
    /** Raw XML source. */
    svg: string;
    /** Weak ETag as sent by the server (`W/"…"`), or null if absent. */
    etag: string | null;
    /** RFC 1123 `Last-Modified` header, or null if absent. */
    lastModified: string | null;
};

/**
 * Fetch the cached SVG for a product. `ifNoneMatch` is optional and
 * lets the caller re-use a previously fetched ETag to skip the body
 * transfer when the file hasn't changed. Callers that always want
 * fresh bytes can omit it.
 */
export async function fetchBoardSvg(
    productName: string,
    options?: { ifNoneMatch?: string; signal?: AbortSignal },
): Promise<BoardSvgFetchResult> {
    if (!productName || productName.trim().length === 0) {
        throw new Error("productName is required");
    }
    const token = useSessionStore.getState().token;
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (options?.ifNoneMatch) headers.set("If-None-Match", options.ifNoneMatch);
    const url = `/api/board-svgs/${encodeURIComponent(productName)}`;
    const response = await fetch(url, {
        method: "GET",
        headers,
        signal: options?.signal,
    });
    if (response.status === 304) {
        return { svg: "", etag: response.headers.get("etag"), lastModified: null };
    }
    if (!response.ok) {
        const body = await response.text().catch(() => "");
        if (response.status === 401) {
            useSessionStore.getState().clear();
        }
        throw new ApiError(response.status, response.statusText, body);
    }
    const svg = await response.text();
    return {
        svg,
        etag: response.headers.get("etag"),
        lastModified: response.headers.get("last-modified"),
    };
}
