import { apiFetch } from "./client";

/**
 * Authentication API helpers. Mirrors the endpoints declared in
 * `Nieweb.Api/Endpoints/AuthEndpoints.cs`:
 *
 *   POST /auth/login   -> LoginResponse   (anonymous)
 *   GET  /auth/whoami  -> WhoAmIResponse  (requires JWT)
 */

export type LoginRequest = {
    email: string;
    password: string;
};

export type LoginResponse = {
    accessToken: string;
    tokenType: string;
    expiresUtc: string;
};

export type WhoAmIResponse = {
    userId: string | null;
    email: string | null;
    name: string | null;
    roles: string[];
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
