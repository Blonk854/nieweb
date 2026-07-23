import { beforeEach, describe, expect, it } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    Outlet,
    RouterProvider,
} from "@tanstack/react-router";

import i18n from "../i18n";
import { SettingsTimezoneRoute } from "./settings-timezone";
import {
    AUTO_TIME_ZONE,
    usePreferencesStore,
} from "../state/preferences";

/**
 * Component-level tests for the /settings/timezone screen. The page
 * is purely browser-local: no API calls, so no fetch stubs are
 * needed. Every test starts from a clean preferences store and
 * verifies that the UI reads from and writes to `usePreferencesStore`
 * as expected.
 */

function renderRoute() {
    const rootRoute = createRootRoute({ component: Outlet });
    const settings = createRoute({
        getParentRoute: () => rootRoute,
        path: "/settings/timezone",
        component: SettingsTimezoneRoute,
    });
    const routeTree = rootRoute.addChildren([settings]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/settings/timezone"] }),
    });
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

/**
 * Wait for the TanStack router to hydrate its Outlet so children of
 * the route are queryable. Every test must call this before it does
 * synchronous queries — the change-password tests use the same
 * pattern by awaiting a `findBy…` up front.
 */
async function waitForRender() {
    await screen.findByRole("heading", { name: /timezone/i, level: 2 });
}

describe("SettingsTimezoneRoute", () => {
    beforeEach(async () => {
        cleanup();
        localStorage.clear();
        usePreferencesStore.setState({ timeZone: AUTO_TIME_ZONE });
        await i18n.changeLanguage("en");
    });

    it("renders in Automatic mode by default with a resolved zone preview", async () => {
        renderRoute();
        await waitForRender();
        // Radio group defaults to Automatic (browser).
        const autoRadio = screen.getByRole("radio", {
            name: /automatic \(follow browser\)/i,
        }) as HTMLInputElement;
        expect(autoRadio.checked).toBe(true);
        // The manual Select is disabled while Automatic is active.
        expect(screen.getByTestId("tz-select")).toBeDisabled();
        // The preview panel shows *something* — jsdom's resolved zone
        // varies by host, so we just assert that a non-empty label is
        // rendered.
        const resolved = screen.getByTestId("tz-resolved");
        expect(resolved.textContent?.length ?? 0).toBeGreaterThan(0);
    });

    it("switches to a manual zone when the user picks one via Manual + Select", async () => {
        renderRoute();
        await waitForRender();
        const user = userEvent.setup();
        // Flip the mode radio to Manual to enable the Select.
        await user.click(
            screen.getByRole("radio", { name: /^time zone$/i }),
        );
        // Mantine's searchable Select renders as a role="combobox".
        const combobox = screen.getByTestId("tz-select");
        expect(combobox).not.toBeDisabled();
        await user.click(combobox);
        // Combobox options are `[data-combobox-option]` divs. Find
        // the Europe/Paris one by exact text match inside the popover.
        const option = await screen.findByText(
            (_content, el) =>
                el?.getAttribute("data-combobox-option") === "true" &&
                el.textContent?.trim() === "Europe/Paris",
        );
        await user.click(option);
        // Store now holds the manual zone; localStorage was updated
        // via the persist middleware.
        await waitFor(() =>
            expect(usePreferencesStore.getState().timeZone).toBe(
                "Europe/Paris",
            ),
        );
        expect(screen.getByTestId("tz-resolved").textContent).toBe(
            "Europe/Paris",
        );
        // A "saved" alert is shown.
        expect(await screen.findByTestId("tz-saved")).toBeInTheDocument();
    });

    it("Reset to automatic wipes a manual choice", async () => {
        // Pre-seed the store with a manual zone so the Reset button
        // is enabled from the first render.
        usePreferencesStore.setState({ timeZone: "Asia/Tokyo" });
        renderRoute();
        await waitForRender();
        expect(usePreferencesStore.getState().timeZone).toBe("Asia/Tokyo");
        const user = userEvent.setup();
        await user.click(screen.getByTestId("tz-reset"));
        expect(usePreferencesStore.getState().timeZone).toBe(AUTO_TIME_ZONE);
        // After reset the Select is disabled again.
        expect(screen.getByTestId("tz-select")).toBeDisabled();
    });

    it("preview reformats when the zone changes", async () => {
        renderRoute();
        await waitForRender();
        // Baseline preview text under Automatic mode.
        const before = screen.getByTestId("tz-preview").textContent;
        expect(before).toBeTruthy();
        // Flip to Pacific/Auckland — 12+ hours ahead of most jsdom
        // hosts, so the preview text is virtually guaranteed to
        // differ. Setting the store directly bypasses the UI and
        // proves the formatter reacts to any store update.
        usePreferencesStore.setState({ timeZone: "Pacific/Auckland" });
        await waitFor(() =>
            expect(screen.getByTestId("tz-resolved").textContent).toBe(
                "Pacific/Auckland",
            ),
        );
        const after = screen.getByTestId("tz-preview").textContent;
        expect(after).toBeTruthy();
        expect(after).not.toBe(before);
    });
});
