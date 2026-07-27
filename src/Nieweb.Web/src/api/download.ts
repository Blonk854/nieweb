import { useSessionStore } from "../state/session";
import { ApiError } from "./client";

/**
 * Download a file from an authenticated API endpoint.
 *
 * A plain `<a href>` cannot carry the session's bearer token, so
 * navigating directly to an export URL returns HTTP 401. This helper
 * fetches the URL with the `Authorization` header (like {@link apiFetch}
 * and the PDF preview modal), turns the response into a blob, and
 * triggers a browser download via a temporary object-URL anchor. The
 * server-provided `Content-Disposition` filename wins; otherwise
 * `fallbackFilename` is used.
 *
 * Throws {@link ApiError} on a non-2xx response so callers can surface
 * the failure (and the session is cleared on 401, matching apiFetch).
 */
export async function downloadWithAuth(
    url: string,
    fallbackFilename: string,
): Promise<void> {
    const token = useSessionStore.getState().token;
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);

    const response = await fetch(url, { headers });
    if (!response.ok) {
        const body = await response.text().catch(() => "");
        if (response.status === 401) {
            useSessionStore.getState().clear();
        }
        throw new ApiError(response.status, response.statusText, body);
    }

    const filename =
        extractFilename(response.headers.get("Content-Disposition")) ?? fallbackFilename;
    const blob = await response.blob();
    const blobUrl = URL.createObjectURL(blob);
    try {
        const anchor = document.createElement("a");
        anchor.href = blobUrl;
        anchor.download = filename;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    } finally {
        URL.revokeObjectURL(blobUrl);
    }
}

/** Parse a `filename` (or `filename*`) out of a Content-Disposition header. */
function extractFilename(disposition: string | null): string | undefined {
    if (!disposition) return undefined;
    const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(disposition);
    if (star?.[1]) {
        try {
            return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ""));
        } catch {
            // fall through to the plain filename
        }
    }
    const plain = /filename="?([^";]+)"?/i.exec(disposition);
    return plain?.[1]?.trim();
}
