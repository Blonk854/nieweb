/**
 * Search-param shape for the /login route. Only `redirect` is honored:
 * it captures the path (with any query string) the user was trying to
 * reach when the auth guard bounced them here, so the LoginRoute can
 * send them back after a successful sign-in.
 */
export type LoginSearch = {
    redirect?: string;
};

const MAX_REDIRECT_LENGTH = 512;

/**
 * Validate + coerce raw URL search params into a `LoginSearch`.
 *
 * Only *relative* URLs starting with a single `/` are honored — this
 * blocks open-redirect abuse via `?redirect=https://evil.example.com`
 * or protocol-relative `?redirect=//evil.example.com`. Anything else
 * is silently dropped so the route still loads.
 */
export function validateLoginSearch(
    raw: Record<string, unknown>,
): LoginSearch {
    const rawRedirect = raw.redirect;
    if (typeof rawRedirect !== "string") {
        return {};
    }
    if (!isSafeRelativePath(rawRedirect)) {
        return {};
    }
    return { redirect: rawRedirect };
}

/**
 * A safe post-login redirect target must:
 *  - be non-empty,
 *  - start with exactly one `/` (relative to the site root),
 *  - not begin with `//` (which would be a protocol-relative URL),
 *  - not exceed a reasonable length.
 */
export function isSafeRelativePath(value: string): boolean {
    if (value.length === 0 || value.length > MAX_REDIRECT_LENGTH) {
        return false;
    }
    if (!value.startsWith("/")) {
        return false;
    }
    if (value.startsWith("//")) {
        return false;
    }
    // Bare backslashes are treated as forward slashes by some browsers,
    // enabling `/\evil.example.com` bypasses; reject up-front.
    if (value.includes("\\")) {
        return false;
    }
    return true;
}
