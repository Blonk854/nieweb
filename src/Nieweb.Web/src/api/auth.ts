import { apiFetch } from "./client";

/**
 * Authentication API helpers. Mirrors the endpoints declared in
 * `Nieweb.Api/Endpoints/AuthEndpoints.cs`:
 *
 *   POST /auth/login           -> LoginResponse   (anonymous)
 *   GET  /auth/whoami          -> WhoAmIResponse  (requires JWT)
 *   POST /auth/change-password -> 204             (requires JWT)
 */

export type LoginRequest = {
    email: string;
    password: string;
};

export type LoginResponse = {
    accessToken: string;
    tokenType: string;
    expiresUtc: string;
    /**
     * True if the account is flagged for forced password rotation. The
     * SPA must route the user to /account/password before letting them
     * reach any protected page.
     */
    mustRotatePassword: boolean;
};

export type WhoAmIResponse = {
    userId: string | null;
    email: string | null;
    name: string | null;
    roles: string[];
    mustRotatePassword: boolean;
};

export type ChangePasswordRequest = {
    currentPassword: string;
    newPassword: string;
};

/**
 * Exchanges credentials for a JWT bearer token. Throws on non-2xx
 * (ApiError) - callers should catch to display friendly errors.
 */
export function login(request: LoginRequest): Promise<LoginResponse> {
    return apiFetch<LoginResponse>("/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
    });
}

/**
 * Fetches the caller's identity using the bearer token currently
 * held by the session store.
 */
export function whoami(): Promise<WhoAmIResponse> {
    return apiFetch<WhoAmIResponse>("/auth/whoami");
}

/**
 * Rotates the caller's password. Returns 204 on success; the SPA
 * should then re-run `whoami()` (which will report
 * `mustRotatePassword: false`) or update the session store's flag
 * locally before navigating away from the change-password screen.
 */
export function changePassword(request: ChangePasswordRequest): Promise<void> {
    return apiFetch<void>("/auth/change-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
    });
}
