import { expect, test } from "@playwright/test";
import {
    FIXTURE_END_UTC,
    FIXTURE_SOURCE_ID,
    FIXTURE_START_UTC,
    loginForToken,
    signInViaSpa,
} from "./support";

/**
 * Pareto happy-path smoke (phase-2.md T4).
 *
 *   log in → open Pareto with axis=Defect over the fixture window →
 *   verify "Total defects" KPI + Export CSV anchor → hit the JSON
 *   API directly and assert numeric parity with the fixture (15
 *   defect-bits across 200 opportunities → overall DPMO = 75 000).
 *
 * Runs against FakeAoiSource. Same auth caveat as panel-yield: the
 * Export CSV anchor is a `<a href="...">` link, so the browser
 * click flow currently 401s. The spec asserts the anchor shape
 * (structural check) and fetches the CSV via the API with an
 * explicit bearer (backend + fixture check).
 */

const PARETO_SEARCH = {
    sourceId: FIXTURE_SOURCE_ID,
    startUtc: FIXTURE_START_UTC,
    endUtc: FIXTURE_END_UTC,
    axis: "Defect" as const,
};

test.describe("Pareto happy-path smoke", () => {
    test("log in → open Pareto (Defect axis) → verify results and export", async ({
        page,
        request,
    }) => {
        await signInViaSpa(page);
        const token = await loginForToken(request);

        await page.goto(paretoRouteUrl(PARETO_SEARCH));
        await expect(
            page.getByRole("heading", { name: "Pareto" }).first(),
        ).toBeVisible();
        // Wait for the results panel to render — "Total defects" is
        // the leading KPI inside ResultsCard and only appears once
        // the report has resolved.
        await expect(page.getByText(/Total defects/i).first()).toBeVisible();

        // Structural check: the Export CSV anchor points at the API
        // endpoint with the canonical query-string.
        const expectedCsvPath = paretoExportPath(PARETO_SEARCH, "csv");
        await expect(
            page.getByRole("link", { name: /Export CSV/i }).first(),
        ).toHaveAttribute("href", expectedCsvPath);

        // Backend check: hit the JSON API with the bearer and assert
        // numeric parity with the fixture. The fixture emits 15
        // defect-bits (see FakeAoiSource.BuildTestedObjects doc).
        const jsonPath = `/api/reports/pareto?${paretoSearchParams(PARETO_SEARCH)}`;
        const jsonResp = await request.get(jsonPath, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(jsonResp.status(), `${jsonPath} should return 200`).toBe(200);
        const json = (await jsonResp.json()) as {
            axis: string;
            overall: { defectBitCount: number; opportunityCount: number; dpmoPpm: number };
            rows: Array<{ groupKey: string; defectCount: number }>;
        };
        expect(json.axis).toBe("Defect");
        expect(json.overall.defectBitCount).toBe(15);
        expect(json.overall.opportunityCount).toBe(200);
        // 15 / 200 * 1e6 = 75 000.
        expect(Math.round(json.overall.dpmoPpm)).toBe(75_000);
        // Five distinct defect bits set on the defective panels.
        expect(json.rows.length).toBe(5);
        // The vital-few bit (Object missing, bit 1) fires on all
        // five defective panels; it must be the top row after the
        // server sorts descending by defect count.
        expect(json.rows[0].defectCount).toBe(5);

        // Export check: fetch the CSV body via the API and assert
        // it has one data row per defect bucket.
        const csvResp = await request.get(expectedCsvPath, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(csvResp.status()).toBe(200);
        expect(csvResp.headers()["content-type"] ?? "").toMatch(/text\/csv/i);
        const csvBody = await csvResp.text();
        const csvLines = csvBody.split(/\r?\n/).filter((l) => l.length > 0);
        // 1 header + 5 defect-bucket data rows.
        expect(csvLines.length).toBeGreaterThanOrEqual(6);
    });

    test("log in → open Pareto (Subpanel axis) → verify terminal axis and slot rows", async ({
        page,
        request,
    }) => {
        await signInViaSpa(page);
        const token = await loginForToken(request);

        const search = { ...PARETO_SEARCH, axis: "Subpanel" as const };
        await page.goto(paretoRouteUrl(search));
        await expect(
            page.getByRole("heading", { name: "Pareto" }).first(),
        ).toBeVisible();
        await expect(page.getByText(/Total defects/i).first()).toBeVisible();
        await expect(page.getByTestId("pareto-axis")).toHaveValue("Subpanel");
        await expect(page.getByTestId("pareto-weight")).toBeEnabled();

        const jsonPath = `/api/reports/pareto?${paretoSearchParams(search)}`;
        const jsonResp = await request.get(jsonPath, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(jsonResp.status(), `${jsonPath} should return 200`).toBe(200);
        const json = (await jsonResp.json()) as {
            axis: string;
            rows: Array<{ groupKey: string; defectCount: number }>;
        };
        expect(json.axis).toBe("Subpanel");
        expect(json.rows.length).toBeGreaterThan(0);
        for (const row of json.rows) {
            expect(row.groupKey).toMatch(/^\d+$/);
        }
    });
});

// ---------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------

type ParetoSmokeSearch = {
    sourceId: string;
    startUtc: string;
    endUtc: string;
    axis: "Defect" | "Product" | "AoiMachine" | "Subpanel";
};

function paretoRouteUrl(search: ParetoSmokeSearch): string {
    return `/app/report/pareto?${paretoSearchParams(search)}`;
}

function paretoExportPath(
    search: ParetoSmokeSearch,
    format: "csv" | "xlsx",
): string {
    return `/api/reports/pareto/export.${format}?${paretoSearchParams(search)}`;
}

function paretoSearchParams(search: ParetoSmokeSearch): string {
    return new URLSearchParams({
        sourceId: search.sourceId,
        startUtc: search.startUtc,
        endUtc: search.endUtc,
        axis: search.axis,
    }).toString();
}
