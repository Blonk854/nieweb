import { expect, test } from "@playwright/test";
import {
    FIXTURE_END_UTC,
    FIXTURE_SOURCE_ID,
    FIXTURE_START_UTC,
    loginForToken,
    signInViaSpa,
} from "./support";

/**
 * Analyse dashboard smoke (ANA-05 + ANA-06 polish pass).
 *
 *   log in → open /app/analyse → verify Panel + Cp/Cpk cards render →
 *   hit the JSON APIs directly and assert structural parity with the
 *   FakeAoiSource fixture (10 panels on 2026-01-15 UTC, five defective
 *   with 15 defect-bits across bits 1/2/3/8/9; every tested object
 *   carries deterministic Delta_* samples).
 *
 * Runs against FakeAoiSource. The Analyse page has no filter form —
 * source auto-selects from /api/sources — so the spec navigates to
 * the bare route and waits for the cards.
 */

test.describe("Analyse dashboard smoke", () => {
    test("log in → open Analyse → Panel and Cp/Cpk cards render with API parity", async ({
        page,
        request,
    }) => {
        await signInViaSpa(page);
        const token = await loginForToken(request);

        await page.goto("/app/analyse");
        await expect(page.getByRole("heading", { name: "Analyse" })).toBeVisible();

        // Panel card: worst-panel ranking renders once the query resolves.
        await expect(page.getByTestId("analyse-panel-summary-card")).toBeVisible();
        await expect(page.getByTestId("analyse-panel-row-0")).toBeVisible();
        // Panel sort control (polish pass) — defects / barcode / date.
        await expect(
            page.getByRole("radiogroup", { name: "Sort panel cards by" }),
        ).toBeVisible();

        // Cp/Cpk card: per-axis capability rows render once resolved.
        await expect(page.getByTestId("analyse-cp-cpk-card")).toBeVisible();
        await expect(page.getByTestId("analyse-cp-cpk-row-0")).toBeVisible();

        // Backend check: panel-summary returns the fixture's 10 panels
        // ranked worst-first (defective panels carry defect bits).
        const panelPath =
            `/api/analyse/panel-summary?sourceId=${FIXTURE_SOURCE_ID}` +
            `&startUtc=${encodeURIComponent(FIXTURE_START_UTC)}` +
            `&endUtc=${encodeURIComponent(FIXTURE_END_UTC)}` +
            `&onlyLastInspection=false`;
        const panelResp = await request.get(panelPath, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(panelResp.status(), `${panelPath} should return 200`).toBe(200);
        const panel = (await panelResp.json()) as {
            totalPanels: number;
            panels: Array<{ panelId: number; barcode: string; defectBitCount: number }>;
            dedupeAppliedInMemory: boolean;
        };
        expect(panel.totalPanels).toBe(10);
        expect(panel.panels.length).toBeGreaterThan(0);
        expect(panel.dedupeAppliedInMemory).toBe(false);
        // Worst-first ordering: first row carries the most defect bits.
        const counts = panel.panels.map((p) => p.defectBitCount);
        const max = Math.max(...counts);
        expect(panel.panels[0].defectBitCount).toBe(max);
        expect(max).toBeGreaterThan(0);

        // Backend check: cp-cpk returns 5 axes × 2 opportunities = 10 rows,
        // each with a non-zero sample count from the fixture's Delta_* data.
        const cpCpkPath =
            `/api/analyse/cp-cpk?sourceId=${FIXTURE_SOURCE_ID}` +
            `&startUtc=${encodeURIComponent(FIXTURE_START_UTC)}` +
            `&endUtc=${encodeURIComponent(FIXTURE_END_UTC)}` +
            `&onlyLastInspection=false`;
        const cpCpkResp = await request.get(cpCpkPath, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(cpCpkResp.status(), `${cpCpkPath} should return 200`).toBe(200);
        const cpCpk = (await cpCpkResp.json()) as {
            rows: Array<{
                axis: string;
                opportunity: string;
                sampleCount: number;
                toleranceConfigured: boolean;
            }>;
            dedupeAppliedInMemory: boolean;
        };
        expect(cpCpk.rows.length).toBe(10);
        expect(cpCpk.dedupeAppliedInMemory).toBe(false);
        for (const row of cpCpk.rows) {
            expect(row.sampleCount, `${row.opportunity}/${row.axis} should have samples`).toBeGreaterThan(0);
        }
        // Seeded tolerance defaults are 0 → not configured in E2E env.
        expect(cpCpk.rows.every((r) => r.toleranceConfigured === false)).toBe(true);
    });
});
