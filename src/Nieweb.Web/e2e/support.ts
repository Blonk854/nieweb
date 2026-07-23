import { expect, type APIRequestContext } from "@playwright/test";

/**
 * Shared constants and helpers for the Playwright happy-path smokes.
 *
 * The bootstrap admin is seeded via env vars in
 * ../playwright.config.ts and the SQLite file is wiped by
 * ../scripts/clean-e2e-db.mjs before every `test:e2e` run, so these
 * credentials always resolve.
 *
 * The FakeAoiSource fixture (Nieweb.DataSources.Fake) exposes ten
 * panels on 2026-01-15 UTC — five clean, five with a single defect
 * across five different defect bits. Total defect-bits set = 15
 * across 200 opportunities → overall DPMO = 75 000 PPM, FPY = 50%.
 */

export const ADMIN_EMAIL = "e2e-admin@nieweb.test";
export const ADMIN_PASSWORD = "e2eE2ePassword";

export const FIXTURE_SOURCE_ID = "fake";
export const FIXTURE_START_UTC = "2026-01-15T00:00:00.000Z";
export const FIXTURE_END_UTC = "2026-01-15T15:00:00.000Z";

/**
 * Calls POST /auth/login directly to acquire a bearer token for
 * subsequent API requests. Used by specs that need to hit backend
 * endpoints outside the browser (CSV/XLSX download, JSON API smokes).
 */
export async function loginForToken(
    request: APIRequestContext,
    email: string = ADMIN_EMAIL,
    password: string = ADMIN_PASSWORD,
): Promise<string> {
    const response = await request.post("/auth/login", {
        data: { email, password },
    });
    expect(response.status(), "login should succeed").toBe(200);
    const body = (await response.json()) as { accessToken: string };
    expect(
        body.accessToken,
        "login response should include accessToken",
    ).toBeTruthy();
    return body.accessToken;
}

/**
 * Sign in via the SPA login form and land on the home screen. Uses
 * placeholder-based locators to avoid strict-mode collisions with
 * Mantine's PasswordInput visibility-toggle button.
 */
export async function signInViaSpa(
    page: import("@playwright/test").Page,
    email: string = ADMIN_EMAIL,
    password: string = ADMIN_PASSWORD,
): Promise<void> {
    await page.goto("/app/login");
    await page.getByPlaceholder("you@example.com").fill(email);
    await page.getByPlaceholder("Enter your password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();
    // Bootstrap admin has MustRotatePassword=false in the E2E env,
    // so login lands on the home screen.
    await expect(page).toHaveURL(/\/app\/?$/);
}
