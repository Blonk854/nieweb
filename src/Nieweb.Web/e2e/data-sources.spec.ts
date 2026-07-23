import { expect, test, type Page } from "@playwright/test";

import {
    ADMIN_EMAIL,
    ADMIN_PASSWORD,
    loginForToken,
    signInViaSpa,
} from "./support";

/**
 * Settings → Databases admin screen happy-path smoke (Phase C —
 * docs/phase-3.md).
 *
 *   log in → open Settings → Databases → verify the seeded "fake"
 *   row → create a new Fake-kind source → edit its display name →
 *   test-connection on it → delete it → verify the "Restart API"
 *   banner appeared along the way.
 *
 * Runs against the E2E environment configured in
 * ../playwright.config.ts, which seeds one row via
 * AoiSourceBootstrapper:
 *   - "fake"        (Fake kind, IsEnabled=true — the flag
 *                    Nieweb__Aoi__Fake__Enabled=true is set for E2E)
 *   - "postreflow"  ← NOT seeded (no ambient SQL creds)
 *   - "prereflow"   ← NOT seeded (no ambient SQL creds)
 *
 * The spec deliberately does NOT click "Restart API now" — doing so
 * shuts down the API host that Playwright's webServer just started,
 * which would tear down every subsequent test in the same run.
 * Confirming the banner is visible after a mutation is sufficient.
 *
 * The spec is idempotent: it uses a stable scratch key
 * ("e2e-scratch") and deletes any pre-existing row with that key
 * over the API before the browser scenario begins. This keeps the
 * spec stable across the `reuseExistingServer: true` local-dev
 * loop where a previous run may have left the row behind.
 */

const SCRATCH_KEY = "e2e-scratch";

async function deleteScratchRow(
    request: import("@playwright/test").APIRequestContext,
    token: string,
): Promise<void> {
    // 204 (deleted) or 404 (didn't exist) are both fine.
    const res = await request.delete(
        `/api/admin/data-sources/${SCRATCH_KEY}`,
        { headers: { Authorization: `Bearer ${token}` } },
    );
    expect([204, 404], await res.text()).toContain(res.status());
}

async function openDatabasesPage(page: Page): Promise<void> {
    await page.goto("/app/settings/databases");
    await expect(
        page.getByRole("heading", { name: "Databases", level: 2 }),
    ).toBeVisible();
    // Wait for the initial GET /api/admin/data-sources to resolve so
    // the seeded "fake" row is in the DOM before we start clicking.
    await expect(page.getByTestId("db-row-fake")).toBeVisible();
}

test.describe("Settings → Databases admin screen", () => {
    test("lists the seeded fake source", async ({ page }) => {
        await signInViaSpa(page);
        await openDatabasesPage(page);

        // The Fake row renders with the localised kind label and the
        // "Enabled" badge (the E2E env sets Fake:Enabled=true).
        const fakeRow = page.getByTestId("db-row-fake");
        await expect(fakeRow).toContainText("fake");
        await expect(fakeRow).toContainText("Fake (in-memory)");
        await expect(fakeRow.getByText("Enabled", { exact: true })).toBeVisible();
    });

    test("create → edit → test-connection → delete → restart-pending banner", async ({
        page,
        request,
    }) => {
        // Reset any leftover scratch row from a previous local run.
        const token = await loginForToken(request, ADMIN_EMAIL, ADMIN_PASSWORD);
        await deleteScratchRow(request, token);

        await signInViaSpa(page);
        await openDatabasesPage(page);

        // ---- 1. Create a Fake-kind scratch source ----------------------
        await page.getByRole("button", { name: "Add database" }).click();
        const createModal = page.getByRole("dialog", { name: "Add database" });
        await expect(createModal).toBeVisible();

        // Fields use getByRole because Mantine renders the required
        // indicator inside the visible label ("Key *", "Display name *"),
        // which breaks a strict getByLabel("Key") match. The textbox's
        // accessible name is the label without the asterisk.
        await createModal
            .getByRole("textbox", { name: "Key" })
            .fill(SCRATCH_KEY);
        await createModal
            .getByRole("textbox", { name: "Display name" })
            .fill("E2E scratch source");
        // Switch Kind from the default "SQL Server" to "Fake" so the
        // server/database/user/password fields drop out and we don't
        // need to invent credentials.
        await createModal.getByRole("combobox", { name: "Kind" }).click();
        await page
            .getByRole("option", { name: "Fake (in-memory)" })
            .click();

        await createModal.getByRole("button", { name: "Create" }).click();
        await expect(createModal).toBeHidden();

        const scratchRow = page.getByTestId(`db-row-${SCRATCH_KEY}`);
        await expect(scratchRow).toBeVisible();
        await expect(scratchRow).toContainText("E2E scratch source");
        await expect(scratchRow).toContainText("Fake (in-memory)");

        // The pending-restart banner must appear after any mutation.
        await expect(
            page.getByTestId("databases-restart-pending"),
        ).toBeVisible();

        // ---- 2. Edit the display name ----------------------------------
        await scratchRow.getByRole("button", { name: "Edit" }).click();
        const editModal = page.getByRole("dialog", {
            name: `Edit database — ${SCRATCH_KEY}`,
        });
        await expect(editModal).toBeVisible();
        // Key field is read-only after create.
        await expect(editModal.getByRole("textbox", { name: "Key" }))
            .toHaveAttribute("readonly", "");

        const displayNameInput = editModal.getByRole("textbox", {
            name: "Display name",
        });
        await displayNameInput.fill("E2E scratch source (edited)");

        // ---- 3. Test the connection from inside the edit modal ---------
        // Fake sources short-circuit the network call and always
        // succeed - see EfAoiSourceConfigs.TestAsync.
        await editModal
            .getByRole("button", { name: "Test connection" })
            .click();
        await expect(editModal.getByTestId("databases-test-result"))
            .toContainText(/Connection succeeded in \d+ ms\./);

        await editModal.getByRole("button", { name: "Save changes" }).click();
        await expect(editModal).toBeHidden();
        await expect(scratchRow).toContainText("E2E scratch source (edited)");

        // ---- 4. Delete the scratch row ---------------------------------
        await scratchRow.getByRole("button", { name: "Delete" }).click();
        const deleteModal = page.getByRole("dialog", {
            name: "Delete database",
        });
        await expect(deleteModal).toBeVisible();
        await expect(deleteModal).toContainText(SCRATCH_KEY);
        await deleteModal.getByRole("button", { name: "Delete" }).click();
        await expect(deleteModal).toBeHidden();
        await expect(scratchRow).toHaveCount(0);

        // Banner is still pending — mutations accumulate until the API
        // is actually restarted. We deliberately do NOT click Restart.
        await expect(
            page.getByTestId("databases-restart-pending"),
        ).toBeVisible();
    });

    test("rejects create with a duplicate key", async ({ page, request }) => {
        // The seeded "fake" row already exists — attempting to create
        // another with the same key must surface the conflict alert.
        // The server's `PUT /api/admin/data-sources/{key}` is a true
        // idempotent upsert, so duplicate detection is enforced
        // client-side by `UpsertModal` (see settings-databases.tsx —
        // it reuses the same conflict alert as the HTTP 409 branch of
        // `parseUpsertError`).
        const token = await loginForToken(request, ADMIN_EMAIL, ADMIN_PASSWORD);
        await deleteScratchRow(request, token);

        await signInViaSpa(page);
        await openDatabasesPage(page);

        await page.getByRole("button", { name: "Add database" }).click();
        const createModal = page.getByRole("dialog", { name: "Add database" });
        await expect(createModal).toBeVisible();

        await createModal.getByRole("textbox", { name: "Key" }).fill("fake");
        await createModal
            .getByRole("textbox", { name: "Display name" })
            .fill("Duplicate key attempt");
        await createModal.getByRole("combobox", { name: "Kind" }).click();
        await page
            .getByRole("option", { name: "Fake (in-memory)" })
            .click();
        await createModal.getByRole("button", { name: "Create" }).click();

        // The alert renders inside the modal with role="alert" and the
        // localised conflict copy. Modal stays open.
        await expect(
            createModal.getByRole("alert").filter({
                hasText: "A database with this key already exists.",
            }),
        ).toBeVisible();
        await expect(createModal).toBeVisible();

        // Close the modal without saving.
        await createModal.getByRole("button", { name: "Cancel" }).click();
        await expect(createModal).toBeHidden();
    });
});
