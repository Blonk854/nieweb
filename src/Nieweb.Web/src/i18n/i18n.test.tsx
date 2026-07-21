import { beforeEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { createMemoryHistory, createRootRoute, createRoute, createRouter, Outlet, RouterProvider } from "@tanstack/react-router";
import { QueryClientProvider } from "@tanstack/react-query";
import i18n, { LANGUAGE_STORAGE_KEY, SUPPORTED_LANGUAGES, isSupportedLanguage } from "../i18n";
import { en } from "../i18n/locales/en";
import { fr } from "../i18n/locales/fr";
import { HomeRoute } from "../routes/home";
import { PanelYieldRoute } from "../routes/panel-yield";
import { createQueryClient } from "../query/queryClient";

/**
 * Render a single route with the given language active. i18n is already
 * initialised in setupTests.ts.
 */
function renderRouteInLanguage(path: string, lang: "en" | "fr") {
    void i18n.changeLanguage(lang);
    const rootRoute = createRootRoute({ component: Outlet });
    const home = createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomeRoute });
    const panelYield = createRoute({ getParentRoute: () => rootRoute, path: "/report/panel-yield", component: PanelYieldRoute });
    const routeTree = rootRoute.addChildren([home, panelYield]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [path] }),
    });
    // Fetch never resolves - HomeRoute stays in isPending, which is
    // fine because we only assert on the always-visible strings.
    return render(
        <MantineProvider>
            <QueryClientProvider client={createQueryClient()}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

describe("i18n bundles", () => {
    beforeEach(() => {
        cleanup();
        window.localStorage.clear();
    });

    it("exposes the supported languages", () => {
        expect(SUPPORTED_LANGUAGES).toContain("en");
        expect(SUPPORTED_LANGUAGES).toContain("fr");
    });

    it("recognises supported language codes", () => {
        expect(isSupportedLanguage("en")).toBe(true);
        expect(isSupportedLanguage("fr")).toBe(true);
        expect(isSupportedLanguage("de")).toBe(false);
    });

    it("FR bundle has the same shape as EN", () => {
        expect(Object.keys(fr).sort()).toEqual(Object.keys(en).sort());
        for (const group of Object.keys(en) as Array<keyof typeof en>) {
            expect(Object.keys(fr[group]).sort()).toEqual(
                Object.keys(en[group]).sort(),
            );
        }
    });

    it("renders the Panel Yield title in French", async () => {
        renderRouteInLanguage("/report/panel-yield", "fr");
        expect(
            await screen.findByRole("heading", {
                level: 2,
                name: /rendement panneau par ligne/i,
            }),
        ).toBeInTheDocument();
    });

    it("renders the Panel Yield title in English", async () => {
        renderRouteInLanguage("/report/panel-yield", "en");
        expect(
            await screen.findByRole("heading", {
                level: 2,
                name: /panel yield by line/i,
            }),
        ).toBeInTheDocument();
    });

    it("persists the chosen language to localStorage", async () => {
        await i18n.changeLanguage("fr");
        expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe("fr");
        await i18n.changeLanguage("en");
        expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe("en");
    });
});
