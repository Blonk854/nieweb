import { describe, it, expect, afterEach, beforeEach, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { QueryClientProvider } from "@tanstack/react-query";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    Outlet,
    RouterProvider,
} from "@tanstack/react-router";
import { createQueryClient } from "../query/queryClient";
import i18n from "../i18n";
import { HomeRoute } from "../routes/home";
import { PanelYieldRoute } from "../routes/panel-yield";
import { LoginRoute } from "../routes/login";

/**
 * Build a router with the same tree shape as ../router/router.tsx but a
 * trivial root component that omits the AppShell chrome. The chrome
 * uses Mantine hooks (useDisclosure, media queries) that don't need to
 * be re-exercised for every route test.
 */
function renderRouteAt(initialPath: string) {
    const rootRoute = createRootRoute({ component: Outlet });
    const home = createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomeRoute });
    const panelYield = createRoute({ getParentRoute: () => rootRoute, path: "/report/panel-yield", component: PanelYieldRoute });
    const login = createRoute({ getParentRoute: () => rootRoute, path: "/login", component: LoginRoute });
    const routeTree = rootRoute.addChildren([home, panelYield, login]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [initialPath] }),
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={createQueryClient()}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

describe("Router", () => {
    beforeEach(async () => {
        await i18n.changeLanguage("en");
    });
    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it("renders the Home route with sources fetched from /api/sources", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(() =>
                Promise.resolve(
                    new Response(
                        JSON.stringify([
                            { id: "postreflow", displayName: "Post-reflow AOI" },
                        ]),
                        {
                            status: 200,
                            headers: { "content-type": "application/json" },
                        },
                    ),
                ),
            ),
        );

        renderRouteAt("/");

        await waitFor(() =>
            expect(
                screen.getByRole("heading", { level: 2, name: /welcome to nieweb/i }),
            ).toBeInTheDocument(),
        );
        await waitFor(() =>
            expect(screen.getByText(/Post-reflow AOI/)).toBeInTheDocument(),
        );
    });

    it("surfaces an alert when /api/sources fails", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(() =>
                Promise.resolve(
                    new Response("boom", { status: 500, statusText: "Server Error" }),
                ),
            ),
        );

        renderRouteAt("/");

        // Home now renders two cards (pinned reports + sources); when
        // fetch fails for everything, both surface an alert. We just
        // want to see that the sources HTTP 500 propagates.
        await waitFor(() => {
            const alerts = screen.getAllByRole("alert");
            expect(alerts.some((a) => /HTTP 500/.test(a.textContent ?? ""))).toBe(true);
        });
    });

    it("navigates to the Panel Yield route at /report/panel-yield", async () => {
        vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("not called"))));

        renderRouteAt("/report/panel-yield");

        await waitFor(() =>
            expect(
                screen.getByRole("heading", { level: 2, name: /panel yield by line/i }),
            ).toBeInTheDocument(),
        );
    });

    it("navigates to the Login route at /login", async () => {
        vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("not called"))));

        renderRouteAt("/login");

        await waitFor(() =>
            expect(
                screen.getByRole("heading", { level: 2, name: /sign in/i }),
            ).toBeInTheDocument(),
        );
    });
});
