import { expect, test } from "@playwright/test";
import { signInViaSpa } from "./support";

/**
 * Traceability Board happy-path smoke (phase-2.md T4).
 *
 *   log in → open Board Trace → type a defective-panel barcode →
 *   submit → verify the stage card renders with the panel ID.
 *
 * Runs against FakeAoiSource. The fixture emits ten panels with
 * barcodes E2E-000..E2E-009; E2E-005 is a defective panel
 * (PanelId 105, Anomaly_BR=1) so it exercises both the "found on
 * this stage" and the "Panel status" rendering paths.
 *
 * The `/api/traceability/boards/by-barcode` endpoint fans out
 * across every registered `IAoiSource`; with only the fake source
 * registered in the E2E env it returns a single stage entry.
 */

const DEFECTIVE_BARCODE = "E2E-005";
const DEFECTIVE_PANEL_ID = "105";
const FIXTURE_SOURCE_ID = "fake";

test.describe("Traceability Board happy-path smoke", () => {
    test("log in → look up defective panel barcode → verify stage card", async ({
        page,
    }) => {
        await signInViaSpa(page);

        await page.goto("/app/traceability/board");
        await expect(
            page.getByRole("heading", { name: /Board trace/i }),
        ).toBeVisible();

        // The barcode input and submit are both marked with stable
        // testids from the route component.
        await page
            .getByTestId("traceability-board-input")
            .fill(DEFECTIVE_BARCODE);
        await page.getByTestId("traceability-board-submit").click();

        // URL should now carry the barcode as a search param.
        await expect(page).toHaveURL(/barcode=E2E-005/);

        // Result panel: the "Barcode" summary line renders the
        // looked-up barcode.
        await expect(
            page.getByTestId("traceability-board-barcode"),
        ).toHaveText(DEFECTIVE_BARCODE);

        // Stage card for the fake source appears with the panel ID.
        // The card is a per-source region tagged
        // traceability-board-stage-<sourceId>.
        const stage = page.getByTestId(
            `traceability-board-stage-${FIXTURE_SOURCE_ID}`,
        );
        await expect(stage).toBeVisible();
        // Panel ID meta-row lists the fixture panel id.
        await expect(stage.getByText(DEFECTIVE_PANEL_ID)).toBeVisible();

        // Sub-panels table renders with one card row (the fixture
        // panel has NbOfValidCards=1).
        const cardsTable = page.getByTestId(
            `traceability-board-cards-${FIXTURE_SOURCE_ID}`,
        );
        await expect(cardsTable).toBeVisible();
        await expect(
            page.getByTestId(
                `traceability-board-cards-${FIXTURE_SOURCE_ID}-row-1`,
            ),
        ).toBeVisible();
    });

    test("unknown barcode surfaces the not-found alert", async ({ page }) => {
        await signInViaSpa(page);

        await page.goto("/app/traceability/board?barcode=DOES-NOT-EXIST");
        await expect(
            page.getByRole("heading", { name: /Board trace/i }),
        ).toBeVisible();
        // The board endpoint fans out across every source; when
        // no source finds the barcode the SPA renders a distinct
        // "not found" alert (see traceability-board.tsx).
        await expect(
            page.getByText(/Barcode not found|Not seen on this stage/i).first(),
        ).toBeVisible();
    });
});
