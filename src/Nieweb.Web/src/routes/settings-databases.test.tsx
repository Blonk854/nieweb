import { beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
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
import { SettingsDatabasesRoute } from "./settings-databases";
import { useSessionStore } from "../state/session";

/**
 * Component tests for the admin-only /settings/databases screen.
 * All API calls are stubbed via `vi.stubGlobal("fetch", …)` because
 * the actual endpoints are covered by the .NET integration tests —
 * here we only prove the SPA wiring: forbidden panel for non-admins,
 * table rendering, restart banner state machine.
 */

type FetchInit = RequestInit | undefined;

function renderRoute() {
    const rootRoute = createRootRoute({ component: Outlet });
    const dbRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/settings/databases",
        component: SettingsDatabasesRoute,
    });
    const routeTree = rootRoute.addChildren([dbRoute]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({
            initialEntries: ["/settings/databases"],
        }),
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

function signInAs(roles: string[]) {
    useSessionStore.getState().setSession(
        {
            email: "admin@example.test",
            displayName: "Admin Tester",
            roles,
            mustRotatePassword: false,
        },
        "test-token",
    );
}

function signOut() {
    useSessionStore.setState({ user: null, token: null });
}

/** Stub every fetch to return a canned JSON body based on URL pattern. */
function stubFetch(
    handlers: Array<{
        match: (url: string, init: FetchInit) => boolean;
        respond: () => Response;
    }>,
) {
    vi.stubGlobal(
        "fetch",
        vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                    ? input.toString()
                    : input.url;
            for (const h of handlers) {
                if (h.match(url, init)) {
                    return Promise.resolve(h.respond());
                }
            }
            return Promise.resolve(
                new Response("not stubbed: " + url, { status: 500 }),
            );
        }),
    );
}

function jsonResponse(body: unknown, status = 200): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: { "Content-Type": "application/json" },
    });
}

const SAMPLE_ROWS = [
    {
        key: "postreflow",
        displayName: "Post-reflow (HLYAOI2024)",
        kind: "SqlServer",
        server: "HLYMSSQL2",
        database: "HLYAOI2024",
        user: "svc_hlyaoiprod",
        hasPassword: true,
        connectTimeoutSeconds: 15,
        queryTimeoutSeconds: 30,
        trustServerCertificate: true,
        encrypt: false,
        isEnabled: true,
        lastTestedUtc: null,
        lastTestSucceeded: null,
        lastTestError: null,
        createdUtc: "2026-07-23T00:00:00Z",
        lastModifiedUtc: "2026-07-23T00:00:00Z",
    },
    {
        key: "fake",
        displayName: "Fake (in-memory)",
        kind: "Fake",
        server: null,
        database: null,
        user: null,
        hasPassword: false,
        connectTimeoutSeconds: 15,
        queryTimeoutSeconds: 30,
        trustServerCertificate: true,
        encrypt: false,
        isEnabled: true,
        lastTestedUtc: null,
        lastTestSucceeded: null,
        lastTestError: null,
        createdUtc: "2026-07-23T00:00:00Z",
        lastModifiedUtc: "2026-07-23T00:00:00Z",
    },
];

describe("SettingsDatabasesRoute", () => {
    beforeEach(async () => {
        cleanup();
        signOut();
        vi.unstubAllGlobals();
        await i18n.changeLanguage("en");
    });

    it("renders a localised forbidden panel for signed-in-but-not-admin users", async () => {
        signInAs(["Reader"]);
        stubFetch([]);
        renderRoute();
        expect(
            await screen.findByRole("heading", { name: /databases/i, level: 2 }),
        ).toBeInTheDocument();
        expect(
            screen.getByText(
                /do not have permission to manage database connections/i,
            ),
        ).toBeInTheDocument();
        // The list must NOT be fetched for non-admins.
        expect(
            screen.queryByRole("button", { name: /add database/i }),
        ).not.toBeInTheDocument();
    });

    it("lists configured sources for admins and hides the restart banner when idle", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (url) => url.endsWith("/api/admin/data-sources"),
                respond: () => jsonResponse(SAMPLE_ROWS),
            },
            {
                match: (url) =>
                    url.endsWith("/api/admin/data-sources/restart-status"),
                respond: () =>
                    jsonResponse({
                        pending: false,
                        setUtc: null,
                        reason: null,
                    }),
            },
        ]);
        renderRoute();
        // The two rows land as data-testid-scoped table rows.
        expect(
            await screen.findByTestId("db-row-postreflow"),
        ).toBeInTheDocument();
        expect(screen.getByTestId("db-row-fake")).toBeInTheDocument();
        // Restart banner is not visible while pending=false.
        expect(
            screen.queryByTestId("databases-restart-pending"),
        ).not.toBeInTheDocument();
    });

    it("shows the pending restart banner when the server reports pending=true", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (url) => url.endsWith("/api/admin/data-sources"),
                respond: () => jsonResponse(SAMPLE_ROWS),
            },
            {
                match: (url) =>
                    url.endsWith("/api/admin/data-sources/restart-status"),
                respond: () =>
                    jsonResponse({
                        pending: true,
                        setUtc: "2026-07-23T05:00:00Z",
                        reason: "updated 'postreflow'",
                    }),
            },
        ]);
        renderRoute();
        expect(
            await screen.findByTestId("databases-restart-pending"),
        ).toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: /restart api now/i }),
        ).toBeEnabled();
    });

    it("opens the Add-database modal when the admin clicks the toolbar button", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (url) => url.endsWith("/api/admin/data-sources"),
                respond: () => jsonResponse([]),
            },
            {
                match: (url) =>
                    url.endsWith("/api/admin/data-sources/restart-status"),
                respond: () =>
                    jsonResponse({
                        pending: false,
                        setUtc: null,
                        reason: null,
                    }),
            },
        ]);
        renderRoute();
        const user = userEvent.setup();
        // Empty state renders when the API returns no rows.
        expect(
            await screen.findByText(/no databases configured yet/i),
        ).toBeInTheDocument();
        await user.click(
            screen.getByRole("button", { name: /add database/i }),
        );
        // Modal title matches the localised "createTitle" key.
        expect(
            await screen.findByRole("dialog", { name: /add database/i }),
        ).toBeInTheDocument();
    });
});
