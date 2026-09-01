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

/**
 * Public discovery shape returned by `GET /auth/config`. Used by the
 * login page to decide whether to render the "Sign in with SSO"
 * button. When `oidcEnabled` is false the other two fields are empty
 * strings and the button must be hidden.
 */
export type AuthConfigResponse = {
    oidcEnabled: boolean;
    oidcButtonLabel: string;
    oidcChallengePath: string;
    analyseEnabled: boolean;
};

/**
 * Fetches the public auth configuration for this Nieweb host.
 * Anonymous - safe to call before the user is signed in.
 */
export function getAuthConfig(): Promise<AuthConfigResponse> {
    return apiFetch<AuthConfigResponse>("/auth/config");
}
