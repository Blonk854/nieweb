import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright end-to-end smoke config (T2).
 *
 * The `webServer` block launches Nieweb.Api directly against an
 * in-memory SQLite database via environment overrides so no local
 * PostgreSQL / .env file is required. The SPA must be pre-built into
 * ../Nieweb.Api/wwwroot/app before `test:e2e` runs - the npm
 * `pretest:e2e` script handles that.
 *
 * Runs Chromium only; other browsers can be added by extending the
 * `projects` array. Kept intentionally small so CI wall-clock stays
 * manageable.
 */
export default defineConfig({
    testDir: "./e2e",
    fullyParallel: false, // single-instance server; keep specs serialized.
    forbidOnly: !!process.env.CI,
    retries: process.env.CI ? 1 : 0,
    workers: 1,
    reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
    timeout: 30_000,
    expect: { timeout: 5_000 },
    use: {
        baseURL: "http://127.0.0.1:5100",
        trace: "on-first-retry",
        screenshot: "only-on-failure",
        video: "retain-on-failure",
    },
    projects: [
        {
            name: "chromium",
            use: { ...devices["Desktop Chrome"] },
        },
    ],
    webServer: {
        // Run the API from the repo root; the SPA has already been
        // built into ../Nieweb.Api/wwwroot/app by pretest:e2e.
        command:
            "dotnet run --project ../Nieweb.Api/Nieweb.Api.csproj --no-launch-profile --urls http://127.0.0.1:5100",
        url: "http://127.0.0.1:5100/health/live",
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        stdout: "pipe",
        stderr: "pipe",
        env: {
            ASPNETCORE_ENVIRONMENT: "Development",
            // SQLite in the Nieweb.Api working directory. Cleaned up by the
            // test-results/ gitignore entry (dev DB file lives in-tree).
            Nieweb__Db__Provider: "Sqlite",
            ConnectionStrings__NiewebDb: "Data Source=nieweb-e2e.db",
            // Test-only signing key. Never used outside E2E runs.
            Nieweb__Auth__Jwt__Issuer: "https://nieweb.test",
            Nieweb__Auth__Jwt__Audience: "nieweb-api-e2e",
            Nieweb__Auth__Jwt__SigningKey:
                "nieweb-e2e-signing-key-must-be-32-plus-bytes-of-utf8",
        },
    },
});
