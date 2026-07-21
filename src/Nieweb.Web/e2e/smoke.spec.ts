import { expect, test } from "@playwright/test";

/**
 * End-to-end smoke (T2). Confirms that the published SPA + API
 * combination is reachable and behaves sensibly.
 *
 * Deliberately tiny - the goal is a fast, boring signal that
 * nothing catastrophic broke during a build. Deeper interaction
 * scenarios live in the SPA and API unit / integration suites,
 * both of which run far faster than a browser round-trip.
 */
test.describe("Nieweb smoke", () => {
    test("health probes are wired", async ({ request }) => {
        // /health/live must always be Healthy - it exists to keep the
        // process on-line and is deliberately dependency-free.
        const live = await request.get("/health/live");
        expect(live.ok(), await live.text()).toBeTruthy();
        const livePayload = await live.json();
        expect(livePayload.status).toBe("Healthy");
        expect(Object.keys(livePayload.checks)).toContain("self");

        // /health/ready and /health/db include the DbContext check.
        // We only assert the endpoints respond and expose the expected
        // check names; whether nieweb-db reports Healthy depends on
        // migrations having run, which is a deployment concern the
        // .NET integration tests already cover.
        const ready = await request.get("/health/ready");
        // 200 (Healthy) or 503 (Unhealthy / Degraded) - both mean wired.
        expect([200, 503]).toContain(ready.status());
        const readyPayload = await ready.json();
        expect(Object.keys(readyPayload.checks)).toEqual(
            expect.arrayContaining(["self", "nieweb-db"]),
        );

        const db = await request.get("/health/db");
        expect([200, 503]).toContain(db.status());
        const dbPayload = await db.json();
        expect(Object.keys(dbPayload.checks)).toContain("nieweb-db");
    });

    test("root redirects to /app/ and the SPA renders", async ({ page }) => {
        const response = await page.goto("/");
        // Redirected to /app/ (302 -> 200 index.html).
        expect(page.url()).toMatch(/\/app\/?$/);
        expect(response?.ok()).toBeTruthy();

        // Vite emits <div id="root"></div> at build time; React fills it
        // in on hydration. Wait for something the layout always renders.
        await expect(page.locator("#root")).toBeAttached();
        // The <title> element is set by index.html; the exact string
        // matches the Vite template output ("Nieweb"). We don't assert
        // an exact match to keep the test tolerant of future changes.
        await expect(page).toHaveTitle(/Nieweb/i);
    });

    test("deep SPA URL is served by the fallback (hard refresh path)", async ({ page }) => {
        // A raw GET to /app/report/panel-yield must return index.html
        // (server-side fallback) even though there's no static file
        // at that path. If the fallback were missing this would 404.
        const response = await page.goto("/app/report/panel-yield");
        expect(response?.status(), "SPA fallback missing").toBe(200);
        await expect(page.locator("#root")).toBeAttached();
    });

    test("unauthenticated API calls are rejected with 401", async ({ request }) => {
        // /api/sources requires a JWT; smoke-check the auth wall stays up.
        const res = await request.get("/api/sources");
        expect(res.status()).toBe(401);
    });
});
